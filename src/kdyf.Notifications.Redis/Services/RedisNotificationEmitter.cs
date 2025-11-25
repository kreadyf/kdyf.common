using kdyf.Notifications.Interfaces;
using kdyf.Notifications.Redis.Configuration;
using kdyf.Notifications.Redis.Integration;
using kdyf.Notifications.Redis.Resilience;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Threading.Channels;

namespace kdyf.Notifications.Redis.Services
{
    /// <summary>
    /// Redis-based notification emitter that publishes notifications to a Redis stream.
    /// Stores notification payloads as Redis keys with TTL and stream metadata for consumption.
    /// Supports updateable messages, stream-only storage, and fire-and-forget emission using Channels.
    ///
    /// Architecture: This class delegates to specialized components:
    /// - NotificationChannelProcessor: Processes messages from the channel and determines emission strategy
    /// - RedisOperationsHandler: Executes low-level Redis operations
    /// </summary>
    internal class RedisNotificationEmitter : IRedisNotificationEmitter, IHostedService, IDisposable
    {
        #region Fields
        private readonly string _baseStreamName;
        private readonly ILogger<RedisNotificationEmitter> _logger;
        private readonly NotificationChannelProcessor _channelProcessor;

        // Fire-and-forget fields
        private readonly Channel<NotificationMessage>? _channel;
        private readonly CancellationTokenSource? _backgroundCts;
        private Task? _backgroundTask;
        private bool _disposed;
        #endregion

        /// <summary>
        /// Creates a new instance of the Redis notification emitter.
        /// </summary>
        /// <param name="logger">Logger for diagnostic messages.</param>
        /// <param name="redis">Redis connection multiplexer.</param>
        /// <param name="configuration">Application configuration containing Redis settings.</param>
        /// <param name="retryPolicy">Retry policy for handling transient failures.</param>
        /// <param name="redisOptions">Optional Redis-specific notification options for updateable, stream-only, routing, and fire-and-forget configuration.</param>
        /// <exception cref="ArgumentNullException">Thrown when redis, logger, or retryPolicy is null.</exception>
        public RedisNotificationEmitter(
            ILogger<RedisNotificationEmitter> logger,
            IConnectionMultiplexer redis,
            IConfiguration configuration,
            IRetryPolicy retryPolicy,
            RedisNotificationOptions? redisOptions = null)
        {
            if (redis == null) throw new ArgumentNullException(nameof(redis));
            if (retryPolicy == null) throw new ArgumentNullException(nameof(retryPolicy));

            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            // Use DefaultStreamName from options as fallback (no longer reading from config)
            _baseStreamName = redisOptions?.Storage.DefaultStreamName ?? "notifications:stream:default";

            // Initialize components
            var redisOpsHandler = new RedisOperationsHandler(retryPolicy, redisOptions, logger);

            _channelProcessor = new NotificationChannelProcessor(
                redis,
                redisOptions,
                logger,  // Reuse the emitter's logger
                redisOpsHandler);

            // Initialize fire-and-forget channel (always enabled)
            var channelCapacity = redisOptions?.Performance.ChannelCapacity ?? 10000;
            var channelFullMode = redisOptions?.Performance.ChannelFullMode ?? BoundedChannelFullMode.Wait;

            var channelOptions = new BoundedChannelOptions(channelCapacity)
            {
                FullMode = channelFullMode
            };
            _channel = Channel.CreateBounded<NotificationMessage>(channelOptions);
            _backgroundCts = new CancellationTokenSource();

            _logger.LogInformation(
                "Fire-and-forget enabled. Channel capacity: {Capacity}, Full mode: {FullMode}",
                channelCapacity,
                channelFullMode);
        }

        #region IHostedService Implementation

        /// <summary>
        /// Starts the background channel processor for fire-and-forget emission.
        /// </summary>
        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting Redis notification emitter background processor...");

            _backgroundTask = Task.Run(() =>
                _channelProcessor.ProcessChannelAsync(_channel!.Reader, _baseStreamName, _backgroundCts!.Token),
                cancellationToken);

            return Task.CompletedTask;
        }

        /// <summary>
        /// Stops the background channel processor gracefully.
        /// </summary>
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_channel != null)
            {
                _logger.LogInformation("Stopping Redis notification emitter background processor...");

                // Signal no more writes
                _channel.Writer.Complete();

                // Cancel background processing
                _backgroundCts?.Cancel();

                // Wait for background task to complete
                if (_backgroundTask != null)
                {
                    try
                    {
                        await _backgroundTask;
                        _logger.LogInformation("Redis notification emitter background processor stopped gracefully.");
                    }
                    catch (OperationCanceledException)
                    {
                        // Expected when cancellation token is triggered
                        _logger.LogInformation("Redis notification emitter background processor cancelled.");
                    }
                }
            }
        }

        #endregion

        #region NotifyAsync - Public API

        /// <summary>
        /// Asynchronously emits a notification to the Redis stream with payload storage.
        ///
        /// This method writes to an in-memory channel and returns immediately (&lt;2ms),
        /// while a background thread processes Redis operations asynchronously.
        ///
        /// Supports three storage strategies:
        /// 1. Stream-only: Stores full payload in stream only (no key-value storage)
        /// 2. Updateable: Uses custom update key instead of NotificationId for key-value storage
        /// 3. Standard: Uses NotificationId for key-value storage + stream reference
        /// </summary>
        /// <typeparam name="TEntity">The type of notification entity that implements <see cref="INotificationEntity"/>.</typeparam>
        /// <param name="entity">The notification entity to emit.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="entity"/> is null.</exception>
        /// <exception cref="ObjectDisposedException">Thrown when the emitter has been disposed.</exception>
        public async Task NotifyAsync<TEntity>(TEntity entity, CancellationToken cancellationToken = default)
            where TEntity : class, INotificationEntity
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(RedisNotificationEmitter));

            if (entity == null)
                throw new ArgumentNullException(nameof(entity));

            // NotificationId and Timestamp are already set by CompositeNotificationEmitter
            // No need to set them again here

            // Fire-and-forget: Write to channel and return immediately (always enabled)
            var message = new NotificationMessage
            {
                Entity = entity,
                EntityType = entity.GetType(),
                QueuedTimestamp = DateTime.UtcNow
            };

            await _channel!.Writer.WriteAsync(message, cancellationToken);

            _logger.LogTrace("Notification {Id} queued to channel (fire-and-forget)", entity.NotificationId);
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed)
                return;

            _backgroundCts?.Cancel();
            _backgroundCts?.Dispose();
            _disposed = true;
        }

        #endregion
    }
}
