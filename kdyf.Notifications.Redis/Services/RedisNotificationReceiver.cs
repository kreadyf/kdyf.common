using kdyf.Notifications.Entities;
using kdyf.Notifications.Interfaces;
using kdyf.Notifications.Redis.Configuration;
using kdyf.Notifications.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text.Json;

namespace kdyf.Notifications.Redis.Services
{
    /// <summary>
    /// Redis-based notification receiver that implements the reactive pattern.
    /// Consumes notifications from Redis Streams using consumer groups and returns an IObservable.
    /// Does NOT perform deduplication - that responsibility belongs to CompositeNotificationReceiver.
    /// </summary>
    internal class RedisNotificationReceiver : INotificationReceiver, IDisposable
    {
        private readonly ILogger<RedisNotificationReceiver> _logger;
        private readonly NotificationTypeResolver _typeResolver;
        private readonly RedisStreamParser _streamParser;
        private readonly RedisStreamInitializer _streamInitializer;
        private readonly IConnectionMultiplexer _redis;
        private readonly string _consumerName;
        private readonly string _streamName;
        private readonly string _consumerGroup;
        private readonly int _maxConcurrentProcessing;
        private readonly RedisNotificationOptions? _redisOptions;
        private int _disposed;

        /// <summary>
        /// Creates a new instance of the Redis notification receiver.
        /// </summary>
        /// <param name="redis">Redis connection multiplexer for database operations.</param>
        /// <param name="configuration">Application configuration containing Redis connection and stream settings.</param>
        /// <param name="logger">Logger for diagnostic messages.</param>
        /// <param name="typeResolver">Service for resolving notification types from type names.</param>
        /// <param name="streamParser">Service for parsing Redis RESP2 protocol responses.</param>
        /// <param name="streamInitializer">Service for initializing Redis streams and consumer groups.</param>
        /// <param name="streamName">Full stream name to consume. If null, uses DefaultStreamName from options.</param>
        /// <param name="redisOptions">Optional Redis notification options for retry and timeout configuration.</param>
        /// <exception cref="ArgumentNullException">Thrown when redis, logger, typeResolver, streamParser, or streamInitializer is null.</exception>
        public RedisNotificationReceiver(IConnectionMultiplexer redis, IConfiguration configuration, ILogger<RedisNotificationReceiver> logger, NotificationTypeResolver typeResolver, RedisStreamParser streamParser, RedisStreamInitializer streamInitializer, string? streamName = null, RedisNotificationOptions? redisOptions = null)
        {
            _redis = redis ?? throw new ArgumentNullException(nameof(redis));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _typeResolver = typeResolver ?? throw new ArgumentNullException(nameof(typeResolver));
            _streamParser = streamParser ?? throw new ArgumentNullException(nameof(streamParser));
            _streamInitializer = streamInitializer ?? throw new ArgumentNullException(nameof(streamInitializer));
            _redisOptions = redisOptions;

            _consumerName = $"consumer-{Environment.MachineName}-{Guid.NewGuid():N}";

            // Use provided stream name or fall back to DefaultStreamName from options
            _streamName = streamName ?? redisOptions?.Storage.DefaultStreamName ?? "notifications:stream:default";
            _consumerGroup = configuration.GetSection("Redis")["ConsumerGroup"] ?? "G_api_worker";
            _maxConcurrentProcessing = Convert.ToInt16(configuration.GetSection("Redis")["MaxConcurrentProcessing"] ?? "10");

            _logger.LogInformation(
                "RedisNotificationReceiver initialized: Consumer={Consumer}, Stream={Stream}, Group={Group}",
                _consumerName,
                _streamName,
                _consumerGroup
            );
        }

        /// <summary>
        /// Creates an observable stream of typed notifications from Redis Streams.
        /// Does NOT perform deduplication.
        /// </summary>
        /// <typeparam name="TEntity">The type of notification entity to receive.</typeparam>
        /// <param name="cancellationToken">Cancellation token to stop the subscription.</param>
        /// <param name="tags">Optional tags to filter notifications.</param>
        /// <returns>An observable stream of typed notifications.</returns>
        public IObservable<TEntity> Receive<TEntity>(CancellationToken cancellationToken, params string[] tags)
            where TEntity : class, INotificationEntity
        {
            return Receive(cancellationToken, tags)
                .OfType<TEntity>();
        }

        /// <summary>
        /// Creates an observable stream of all notifications from Redis Streams.
        /// Uses XREADGROUP with consumer groups for reliable distributed processing.
        /// Does NOT perform deduplication.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token to stop the subscription.</param>
        /// <param name="tags">Optional tags to filter notifications.</param>
        /// <returns>An observable stream of all notifications.</returns>
        public IObservable<INotificationEntity> Receive(CancellationToken cancellationToken, params string[] tags)
        {
            ThrowIfDisposed();

            HashSet<string> tagsSet = tags.ToHashSet();

            return Observable.Create<INotificationEntity>(async (observer, ct) =>
            {
                var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, ct);
                var linkedToken = linkedCts.Token;

                try
                {
                    // Use shared IConnectionMultiplexer (thread-safe, designed for multiplexing)
                    var db = _redis.GetDatabase();

                    // Ensure consumer group exists
                    await EnsureConsumerGroupAsync(db, linkedToken);

                    _logger.LogInformation("Redis receiver started observing stream: {Stream}", _streamName);

                    // Continuously read from stream
                    while (!linkedToken.IsCancellationRequested)
                    {
                        try
                        {
                            // XREADGROUP with BLOCK - truly reactive, no polling
                            var blockMs = _redisOptions?.Performance.XReadGroupBlockMs ?? 5000;
                            var result = await db.ExecuteAsync(
                                "XREADGROUP",
                                "GROUP", _consumerGroup, _consumerName,
                                "BLOCK", blockMs,  // Configurable block duration
                                "COUNT", 100,   // Read up to 100 messages
                                "STREAMS", _streamName, ">"
                            );

                            if (!result.IsNull && result.Resp2Type == ResultType.Array)
                            {
                                var entries = _streamParser.ParseStreamEntries(result);

                                foreach (var entry in entries)
                                {
                                    try
                                    {
                                        var notification = await ProcessStreamEntryAsync(db, entry, linkedToken);

                                        if (notification != null)
                                        {
                                            // Filter by tags if specified
                                            if (tagsSet.Count == 0 || tagsSet.Any(k => notification.Tags?.Contains(k) ?? false))
                                            {
                                                // Emit notification to observer - NO deduplication here
                                                observer.OnNext(notification);
                                            }

                                            // ACK the message after successful processing
                                            await db.StreamAcknowledgeAsync(_streamName, _consumerGroup, entry.Id);
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger.LogError(ex, "Failed to process message {MessageId}", entry.Id);
                                        // Don't ACK failed messages - they can be retried
                                    }
                                }
                            }
                        }
                        catch (OperationCanceledException) when (linkedToken.IsCancellationRequested)
                        {
                            break;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error reading from Redis stream");
                            // Configurable backoff before retry
                            var recoveryDelayMs = _redisOptions?.Resilience.ErrorRecoveryDelayMs ?? 1000;
                            await Task.Delay(recoveryDelayMs, linkedToken);
                        }
                    }

                    _logger.LogInformation("Redis receiver stopped observing stream: {Stream}", _streamName);
                    observer.OnCompleted();
                }
                catch (OperationCanceledException) when (linkedToken.IsCancellationRequested)
                {
                    _logger.LogInformation("Redis receiver cancelled");
                    observer.OnCompleted();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Redis receiver encountered a fatal error");
                    observer.OnError(ex);
                }
                finally
                {
                    linkedCts.Dispose();
                }

                return Disposable.Empty;
            });
        }

        private async Task<INotificationEntity?> ProcessStreamEntryAsync(IDatabase db, StreamEntry entry, CancellationToken ct)
        {
            var fields = entry.Values.ToDictionary(x => x.Name.ToString(), x => x.Value.ToString());

            var typeName = fields.GetValueOrDefault("type", "");
            var storage = fields.GetValueOrDefault("storage", "");

            _logger.LogDebug("Processing Redis stream entry: Type={Type}, Storage={Storage}", typeName, storage);

            // Strategy 1: Stream-only storage - payload is in the stream
            string? json = null;
            if (storage == "stream-only")
            {
                json = fields.GetValueOrDefault("payload", "");
                if (string.IsNullOrWhiteSpace(json))
                {
                    _logger.LogWarning("Stream-only entry {EntryId} has no payload", entry.Id);
                    return null;
                }
                _logger.LogDebug("Reading stream-only payload from stream entry");
            }
            else
            {
                // Strategy 2/3: Standard or Updateable - payload is in key-value storage
                var key = fields.GetValueOrDefault("key", "");
                if (string.IsNullOrWhiteSpace(key))
                {
                    _logger.LogWarning("Stream entry {EntryId} has no key field", entry.Id);
                    return null;
                }

                // Load the content from Redis
                var redisValue = await db.StringGetAsync(key);
                if (!redisValue.HasValue)
                {
                    _logger.LogWarning("Redis key {Key} not found for stream entry {EntryId}", key, entry.Id);
                    return null;
                }
                json = redisValue.ToString();
            }

            // Delegate to NotificationTypeResolver for type resolution, deserialization, and fallback
            var notificationId = fields.GetValueOrDefault("id", "");
            var timestampStr = fields.GetValueOrDefault("timestamp", "");
            var timestamp = DateTime.TryParse(timestampStr, out var ts) ? ts : (DateTime?)null;

            return _typeResolver.DeserializeOrCreateFallback(
                json,
                typeName,
                notificationId,
                timestamp);
        }


        /// <summary>
        /// Ensures consumer group exists with retry logic and health checks.
        /// Delegates to RedisStreamInitializer for reusable initialization logic.
        /// </summary>
        private async Task EnsureConsumerGroupAsync(IDatabase db, CancellationToken ct)
        {
            await _streamInitializer.EnsureConsumerGroupAsync(db, _streamName, _consumerGroup, ct);
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref _disposed) == 1)
                throw new ObjectDisposedException(GetType().FullName);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                // No need to dispose _redis - it's managed by DI container
            }
            GC.SuppressFinalize(this);
        }
    }
}
