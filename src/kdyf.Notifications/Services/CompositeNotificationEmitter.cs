using kdyf.Notifications.Interfaces;
using Microsoft.Extensions.Logging;

namespace kdyf.Notifications.Services
{
    /// <summary>
    /// Composite notification emitter that coordinates multiple transport emitters.
    /// Emits notifications to all registered transports in parallel.
    /// This is the central coordination point for all emission strategies (InMemory, Redis, Dapr, etc.).
    /// </summary>
    internal class CompositeNotificationEmitter : INotificationEmitter, IDisposable
    {
        private readonly IEnumerable<INotificationEmitter> _emitters;
        private readonly ILogger<CompositeNotificationEmitter> _logger;
        private int _disposed;

        /// <summary>
        /// Creates a new instance of the composite notification emitter.
        /// </summary>
        /// <param name="emitters">Collection of transport emitters to coordinate.</param>
        /// <param name="logger">Logger for diagnostic messages.</param>
        /// <exception cref="ArgumentNullException">Thrown when emitters or logger is null.</exception>
        public CompositeNotificationEmitter(
            IEnumerable<INotificationEmitter> emitters,
            ILogger<CompositeNotificationEmitter> logger)
        {
            _emitters = emitters ?? throw new ArgumentNullException(nameof(emitters));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            var emittersList = _emitters.ToList();
            if (emittersList.Count == 0)
            {
                _logger.LogWarning("CompositeNotificationEmitter initialized with no emitters");
            }
            else
            {
                _logger.LogInformation(
                    "CompositeNotificationEmitter initialized with {Count} emitter(s): {Emitters}",
                    emittersList.Count,
                    string.Join(", ", emittersList.Select(e => e.GetType().Name))
                );
            }
        }

        /// <summary>
        /// Asynchronously emits a notification to all registered transport emitters in parallel.
        /// If any emitter fails, logs the error but continues emitting to other transports.
        /// </summary>
        /// <typeparam name="TEntity">The type of notification entity.</typeparam>
        /// <param name="entity">The notification entity to emit.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task that completes when all emitters have processed the notification.</returns>
        /// <exception cref="ArgumentNullException">Thrown when entity is null.</exception>
        /// <exception cref="ObjectDisposedException">Thrown when the emitter has been disposed.</exception>
        public async Task NotifyAsync<TEntity>(TEntity entity, CancellationToken cancellationToken = default)
            where TEntity : class, INotificationEntity
        {
            ThrowIfDisposed();

            if (entity == null)
                throw new ArgumentNullException(nameof(entity));

            cancellationToken.ThrowIfCancellationRequested();

            // Ensure stable NotificationId and Timestamp BEFORE dispatching to transports
            // This ensures all transports emit with the same ID for proper deduplication
            entity.NotificationId ??= Guid.NewGuid().ToString();

            if (entity.Timestamp == default)
                entity.Timestamp = DateTime.UtcNow;

            // Emit to all transports in parallel
            var tasks = _emitters.Select(async emitter =>
            {
                try
                {
                    await emitter.NotifyAsync(entity, cancellationToken);
                    _logger.LogDebug(
                        "Successfully emitted notification {NotificationId} to {EmitterType}",
                        entity.NotificationId,
                        emitter.GetType().Name
                    );
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Failed to emit notification {NotificationId} to {EmitterType}",
                        entity.NotificationId,
                        emitter.GetType().Name
                    );
                    // Don't rethrow - allow other emitters to continue
                }
            });

            await Task.WhenAll(tasks);
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
                // Dispose all emitters that implement IDisposable
                foreach (var emitter in _emitters.OfType<IDisposable>())
                {
                    try
                    {
                        emitter.Dispose();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error disposing emitter {EmitterType}", emitter.GetType().Name);
                    }
                }
            }
            GC.SuppressFinalize(this);
        }
    }
}
