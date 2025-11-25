using kdyf.Notifications.Configuration;
using kdyf.Notifications.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System.Reactive.Linq;

namespace kdyf.Notifications.Services
{
    /// <summary>
    /// Composite notification receiver that coordinates multiple transport receivers.
    /// Merges streams from all transports and performs CENTRALIZED DEDUPLICATION.
    /// This is the ONLY component responsible for deduplication in the entire system.
    /// </summary>
    internal class CompositeNotificationReceiver : INotificationReceiver, IDisposable
    {
        private readonly IEnumerable<INotificationReceiver> _receivers;
        private readonly IMemoryCache _deduplicationCache;
        private readonly MemoryCacheEntryOptions _cacheOptions;
        private readonly ILogger<CompositeNotificationReceiver> _logger;
        private readonly object _deduplicationLock = new object();
        private readonly Dictionary<string, IObservable<INotificationEntity>> _sharedStreams = new();
        private readonly object _streamCreationLock = new object();
        private int _disposed;

        /// <summary>
        /// Creates a new instance of the composite notification receiver with dependency-injected cache and options.
        /// </summary>
        /// <param name="receivers">Collection of transport receivers to coordinate.</param>
        /// <param name="cache">Memory cache for deduplication (configured with size limits).</param>
        /// <param name="options">Notification options containing cache configuration.</param>
        /// <param name="logger">Logger for diagnostic messages.</param>
        /// <exception cref="ArgumentNullException">Thrown when any parameter is null.</exception>
        public CompositeNotificationReceiver(
            IEnumerable<INotificationReceiver> receivers,
            IMemoryCache cache,
            NotificationOptions options,
            ILogger<CompositeNotificationReceiver> logger)
        {
            _receivers = receivers ?? throw new ArgumentNullException(nameof(receivers));
            _deduplicationCache = cache ?? throw new ArgumentNullException(nameof(cache));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            if (options == null)
                throw new ArgumentNullException(nameof(options));

            var receiversList = _receivers.ToList();
            if (receiversList.Count == 0)
            {
                _logger.LogWarning("CompositeNotificationReceiver initialized with no receivers");
            }
            else
            {
                _logger.LogInformation(
                    "CompositeNotificationReceiver initialized with {Count} receiver(s): {Receivers}",
                    receiversList.Count,
                    string.Join(", ", receiversList.Select(r => r.GetType().Name))
                );
            }

            // Configure cache entry options from NotificationOptions
            _cacheOptions = new MemoryCacheEntryOptions
            {
                SlidingExpiration = options.DeduplicationTtl,
                Size = 1 // Each entry counts as 1 toward the size limit
            };

            _logger.LogInformation(
                "Deduplication cache configured: TTL={Ttl}, MaxSize={MaxSize}",
                options.DeduplicationTtl,
                options.MaxDeduplicationCacheSize
            );
        }

        /// <summary>
        /// Creates a new instance of the composite notification receiver (legacy constructor for backward compatibility).
        /// </summary>
        /// <param name="receivers">Collection of transport receivers to coordinate.</param>
        /// <param name="logger">Logger for diagnostic messages.</param>
        /// <param name="deduplicationTtl">Time-to-live for deduplication cache entries. Defaults to 10 minutes.</param>
        /// <param name="maxCacheSize">Maximum number of entries in deduplication cache. Defaults to 10,000.</param>
        /// <exception cref="ArgumentNullException">Thrown when receivers or logger is null.</exception>
        public CompositeNotificationReceiver(
            IEnumerable<INotificationReceiver> receivers,
            ILogger<CompositeNotificationReceiver> logger,
            TimeSpan? deduplicationTtl = null,
            int maxCacheSize = 10_000)
        {
            _receivers = receivers ?? throw new ArgumentNullException(nameof(receivers));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            var receiversList = _receivers.ToList();
            if (receiversList.Count == 0)
            {
                _logger.LogWarning("CompositeNotificationReceiver initialized with no receivers");
            }
            else
            {
                _logger.LogInformation(
                    "CompositeNotificationReceiver initialized with {Count} receiver(s): {Receivers}",
                    receiversList.Count,
                    string.Join(", ", receiversList.Select(r => r.GetType().Name))
                );
            }

            // Initialize deduplication cache with size limit (legacy behavior)
            _deduplicationCache = new MemoryCache(new MemoryCacheOptions
            {
                SizeLimit = maxCacheSize
            });

            _cacheOptions = new MemoryCacheEntryOptions
            {
                SlidingExpiration = deduplicationTtl ?? TimeSpan.FromMinutes(10),
                Size = 1 // Each entry counts as 1 toward the size limit
            };

            _logger.LogInformation(
                "Deduplication cache configured: TTL={Ttl}, MaxSize={MaxSize}",
                _cacheOptions.SlidingExpiration,
                maxCacheSize
            );
        }

        /// <summary>
        /// Creates an observable stream of notifications from all transports, filtered by type and optional tags.
        /// Performs centralized deduplication to ensure each notification is delivered only once.
        /// </summary>
        /// <typeparam name="TEntity">The type of notification entity to receive.</typeparam>
        /// <param name="cancellationToken">Cancellation token to stop the subscription.</param>
        /// <param name="tags">Optional tags to filter notifications.</param>
        /// <returns>An observable stream of typed, deduplicated notifications.</returns>
        /// <exception cref="ObjectDisposedException">Thrown when the receiver has been disposed.</exception>
        public IObservable<TEntity> Receive<TEntity>(CancellationToken cancellationToken, params string[] tags)
            where TEntity : class, INotificationEntity
        {
            return Receive(cancellationToken, tags)
                .OfType<TEntity>();
        }

        /// <summary>
        /// Creates an observable stream of all notifications from all transports, filtered by optional tags.
        /// Performs centralized deduplication to ensure each notification is delivered only once.
        /// Returns a shared hot observable that multicasts to all listeners using the same tag filter.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token to stop the subscription.</param>
        /// <param name="tags">Optional tags to filter notifications.</param>
        /// <returns>An observable stream of all deduplicated notifications.</returns>
        /// <exception cref="ObjectDisposedException">Thrown when the receiver has been disposed.</exception>
        public IObservable<INotificationEntity> Receive(CancellationToken cancellationToken, params string[] tags)
        {
            ThrowIfDisposed();

            // Create a cache key based on tags to support different tag filters
            var cacheKey = string.Join(",", (tags ?? Array.Empty<string>()).OrderBy(t => t));

            // Get or create shared stream for this tag combination
            lock (_streamCreationLock)
            {
                if (!_sharedStreams.TryGetValue(cacheKey, out var sharedStream))
                {
                    _logger.LogInformation("Creating shared observable stream for tags: [{Tags}]", cacheKey);

                    // Merge all receiver streams into one
                    var mergedStreams = _receivers
                        .Select(receiver =>
                        {
                            try
                            {
                                return receiver.Receive(cancellationToken, tags ?? Array.Empty<string>());
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(
                                    ex,
                                    "Failed to create receive stream from {ReceiverType}",
                                    receiver.GetType().Name
                                );
                                // Return empty observable for failed receivers
                                return Observable.Empty<INotificationEntity>();
                            }
                        })
                        .Merge();

                    // Apply centralized deduplication with lock to prevent race conditions
                    // Lock ensures atomicity of check-and-set operation
                    var deduplicated = mergedStreams
                        .Where(notification =>
                        {
                            lock (_deduplicationLock)
                            {
                                // Check if already processed
                                if (_deduplicationCache.TryGetValue(notification.NotificationId, out _))
                                {
                                    _logger.LogDebug(
                                        "Duplicate notification detected and filtered: {NotificationId} ({Type})",
                                        notification.NotificationId,
                                        notification.NotificationType
                                    );
                                    return false; // Duplicate - filter out
                                }

                                // Mark as processed
                                _deduplicationCache.Set(notification.NotificationId, true, _cacheOptions);

                                _logger.LogDebug(
                                    "New notification received: {NotificationId} ({Type})",
                                    notification.NotificationId,
                                    notification.NotificationType
                                );

                                return true; // Not a duplicate - pass through
                            }
                        });

                    // Make it hot and shareable: Publish() converts to ConnectableObservable,
                    // RefCount() auto-connects when first subscriber arrives and disconnects when last leaves
                    // This ensures all listeners receive the SAME deduplicated stream
                    sharedStream = deduplicated.Publish().RefCount();

                    _sharedStreams[cacheKey] = sharedStream;

                    _logger.LogInformation("Shared observable stream created for tags: [{Tags}]", cacheKey);
                }

                return sharedStream;
            }
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
                // Dispose deduplication cache
                _deduplicationCache?.Dispose();

                // Dispose all receivers that implement IDisposable
                foreach (var receiver in _receivers.OfType<IDisposable>())
                {
                    try
                    {
                        receiver.Dispose();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error disposing receiver {ReceiverType}", receiver.GetType().Name);
                    }
                }
            }
            GC.SuppressFinalize(this);
        }
    }
}
