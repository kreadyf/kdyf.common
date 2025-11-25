using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace kdyf.Notifications.Interfaces
{
    /// <summary>
    /// Defines the contract for receiving notifications as reactive observables.
    /// </summary>
    public interface INotificationReceiver
    {
        /// <summary>
        /// Creates an observable that receives notifications of a specific type, optionally filtered by tags.
        /// </summary>
        /// <typeparam name="TEntity">The specific type of notification entity to receive.</typeparam>
        /// <param name="cancellationToken">Cancellation token to stop the subscription.</param>
        /// <param name="tags">Optional tags to filter notifications. If specified, only notifications containing all these tags will be emitted.</param>
        /// <returns>An observable that emits notifications of the specified type.</returns>
        IObservable<TEntity> Receive<TEntity>(CancellationToken cancellationToken, params string[] tags) where TEntity : class, INotificationEntity;

        /// <summary>
        /// Creates an observable that receives all notifications, optionally filtered by tags.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token to stop the subscription.</param>
        /// <param name="tags">Optional tags to filter notifications. If specified, only notifications containing all these tags will be emitted.</param>
        /// <returns>An observable that emits notifications filtered by the specified tags.</returns>
        IObservable<INotificationEntity> Receive(CancellationToken cancellationToken, params string[] tags);
    }
}
