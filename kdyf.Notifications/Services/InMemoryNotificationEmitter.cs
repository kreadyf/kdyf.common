using kdyf.Notifications.Interfaces;
using System.Reactive.Subjects;

namespace kdyf.Notifications.Services
{
    /// <summary>
    /// In-memory notification emitter that publishes notifications to a local reactive subject.
    /// This emitter does NOT perform deduplication - that responsibility belongs to CompositeNotificationReceiver.
    /// </summary>
    internal class InMemoryNotificationEmitter : INotificationEmitter, IDisposable
    {
        private readonly ISubject<INotificationEntity> _subject;
        private int _disposed;

        /// <summary>
        /// Creates a new instance of the in-memory notification emitter.
        /// </summary>
        /// <param name="subject">The reactive subject used to publish notifications.</param>
        /// <exception cref="ArgumentNullException">Thrown when subject is null.</exception>
        public InMemoryNotificationEmitter(ISubject<INotificationEntity> subject)
        {
            _subject = subject ?? throw new ArgumentNullException(nameof(subject));
        }

        /// <summary>
        /// Asynchronously emits a notification to the local subject.
        /// Does NOT perform deduplication.
        /// </summary>
        /// <typeparam name="TEntity">The type of notification entity.</typeparam>
        /// <param name="entity">The notification entity to emit.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A completed task.</returns>
        /// <exception cref="ArgumentNullException">Thrown when entity is null.</exception>
        /// <exception cref="ObjectDisposedException">Thrown when the emitter has been disposed.</exception>
        public Task NotifyAsync<TEntity>(TEntity entity, CancellationToken cancellationToken = default)
            where TEntity : class, INotificationEntity
        {
            ThrowIfDisposed();

            if (entity == null)
                throw new ArgumentNullException(nameof(entity));

            cancellationToken.ThrowIfCancellationRequested();

            // NotificationId and Timestamp are set by CompositeNotificationEmitter before dispatch
            // No need to mutate domain entities here in the infrastructure layer

            // Emit to subject - NO deduplication here
            _subject.OnNext(entity);

            return Task.CompletedTask;
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
                // Don't complete the subject - it might be shared
                // The subject lifecycle is managed externally
            }
            GC.SuppressFinalize(this);
        }
    }
}
