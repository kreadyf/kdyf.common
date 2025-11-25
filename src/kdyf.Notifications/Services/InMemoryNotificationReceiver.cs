using kdyf.Notifications.Interfaces;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace kdyf.Notifications.Services
{
    /// <summary>
    /// In-memory notification receiver that observes notifications from a local reactive subject.
    /// This receiver does NOT perform deduplication - that responsibility belongs to CompositeNotificationReceiver.
    /// </summary>
    internal class InMemoryNotificationReceiver : INotificationReceiver, IDisposable
    {
        private readonly IObservable<INotificationEntity> _observable;
        private static readonly IScheduler _scheduler = TaskPoolScheduler.Default;
        private int _disposed;

        /// <summary>
        /// Creates a new instance of the in-memory notification receiver.
        /// </summary>
        /// <param name="subject">The reactive subject to observe notifications from.</param>
        /// <exception cref="ArgumentNullException">Thrown when subject is null.</exception>
        public InMemoryNotificationReceiver(ISubject<INotificationEntity> subject)
        {
            if (subject == null)
                throw new ArgumentNullException(nameof(subject));

            _observable = subject.AsObservable();
        }

        /// <summary>
        /// Creates an observable stream of notifications filtered by type and optional tags.
        /// Does NOT perform deduplication.
        /// </summary>
        /// <typeparam name="TEntity">The type of notification entity to receive.</typeparam>
        /// <param name="cancellationToken">Cancellation token to stop the subscription.</param>
        /// <param name="tags">Optional tags to filter notifications.</param>
        /// <returns>An observable stream of typed notifications.</returns>
        /// <exception cref="ObjectDisposedException">Thrown when the receiver has been disposed.</exception>
        public IObservable<TEntity> Receive<TEntity>(CancellationToken cancellationToken, params string[] tags)
            where TEntity : class, INotificationEntity
        {
            return Receive(cancellationToken, tags)
                .OfType<TEntity>();
        }

        /// <summary>
        /// Creates an observable stream of all notifications filtered by optional tags.
        /// Does NOT perform deduplication.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token to stop the subscription.</param>
        /// <param name="tags">Optional tags to filter notifications.</param>
        /// <returns>An observable stream of all notifications.</returns>
        /// <exception cref="ObjectDisposedException">Thrown when the receiver has been disposed.</exception>
        public IObservable<INotificationEntity> Receive(CancellationToken cancellationToken, params string[] tags)
        {
            ThrowIfDisposed();

            HashSet<string> tagsSet = tags.ToHashSet();

            return Observable.Create<INotificationEntity>(observer =>
            {
                var subscription = _observable
                    .Where(entity => tagsSet.Count == 0 || tagsSet.Any(k => (entity.Tags?.Contains(k) ?? false)))
                    .ObserveOn(_scheduler)
                    .Subscribe(observer.OnNext, observer.OnError, observer.OnCompleted);

                var disposable = Disposable.Create(subscription.Dispose);
                cancellationToken.Register(disposable.Dispose);

                return disposable;
            });
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref _disposed) == 1)
                throw new ObjectDisposedException(GetType().FullName);
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _disposed, 1);
            GC.SuppressFinalize(this);
        }
    }
}
