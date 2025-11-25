using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace kdyf.Notifications.Interfaces
{
    /// <summary>
    /// Defines the contract for emitting notifications to subscribers.
    /// All operations are asynchronous to prevent thread blocking and improve performance.
    /// </summary>
    public interface INotificationEmitter
    {
        /// <summary>
        /// Emits a notification asynchronously to all active subscribers.
        /// </summary>
        /// <typeparam name="TEntity">The type of notification entity that implements <see cref="INotificationEntity"/>.</typeparam>
        /// <param name="entity">The notification entity to emit.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="entity"/> is null.</exception>
        Task NotifyAsync<TEntity>(TEntity entity, CancellationToken cancellationToken = default)
            where TEntity : class, INotificationEntity;
    }
}
