using System;
using System.Collections.Generic;

namespace kdyf.Notifications.Interfaces
{
    /// <summary>
    /// Factory interface for creating notification receivers.
    /// Allows external transports (Redis, Kafka, RabbitMQ, etc.) to create receivers
    /// without the core project needing to know implementation details.
    ///
    /// Multiple factories can be registered simultaneously - each factory
    /// is responsible for checking if its configuration exists in properties
    /// and creating receivers only for its transport.
    /// </summary>
    public interface INotificationReceiverFactory
    {
        /// <summary>
        /// Creates notification receiver instances based on the builder properties.
        /// Each factory implementation should check for its specific properties
        /// (e.g., "Redis.StreamNames", "Kafka.Topics") and return empty if not configured.
        /// </summary>
        /// <param name="serviceProvider">The service provider for resolving dependencies.</param>
        /// <param name="properties">
        /// The builder properties dictionary containing transport-specific configuration.
        /// Each transport uses its own keys (e.g., "Redis.StreamNames", "Kafka.Topics").
        /// </param>
        /// <returns>
        /// A collection of notification receivers for this transport,
        /// or empty collection if this transport is not configured.
        /// </returns>
        IEnumerable<INotificationReceiver> CreateReceivers(
            IServiceProvider serviceProvider,
            IDictionary<string, object> properties);
    }
}
