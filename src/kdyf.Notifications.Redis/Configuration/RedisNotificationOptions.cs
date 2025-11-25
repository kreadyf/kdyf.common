using kdyf.Notifications.Interfaces;

namespace kdyf.Notifications.Redis.Configuration
{
    /// <summary>
    /// Configuration options for Redis notification infrastructure.
    /// Controls updateable message behavior, storage strategies, and stream routing.
    /// This is INFRASTRUCTURE-ONLY configuration - business logic remains unchanged.
    ///
    /// Configuration is organized into three main groups:
    /// - Storage: Controls how notifications are stored (streams, keys, TTL)
    /// - Performance: Controls throughput and initialization behavior
    /// - Resilience: Controls retry policies and circuit breaker behavior
    /// </summary>
    public class RedisNotificationOptions
    {
        /// <summary>
        /// Configuration for updateable notification types.
        /// Maps notification type to its update key strategy.
        /// When configured, notifications will UPDATE existing Redis keys instead of creating new ones.
        /// </summary>
        public Dictionary<Type, UpdateableNotificationConfig> UpdateableTypes { get; set; } = new();

        /// <summary>
        /// Types that should store full payload in stream (no separate key-value storage).
        /// Useful for high-frequency, small payload notifications where reducing Redis operations is important.
        /// Stream-only notifications store everything in the stream entry, eliminating the separate key-value SET operation.
        /// </summary>
        public HashSet<Type> StreamOnlyTypes { get; set; } = new();

        /// <summary>
        /// Maps notification types to specific stream names for routing.
        /// Enables separation of concerns by routing different notification types to different Redis streams.
        /// IMPORTANT: Stream names must be FULL stream names, not suffixes.
        /// Example: typeof(OrderNotification) -> "notifications:stream:orders"
        /// All streams must be configured at startup - no dynamic stream creation allowed.
        /// </summary>
        public Dictionary<Type, string> TypeToStreamMapping { get; set; } = new();

        /// <summary>
        /// Storage configuration: Controls how notifications are stored (streams, keys, TTL).
        /// </summary>
        public StorageOptions Storage { get; set; } = new();

        /// <summary>
        /// Performance configuration: Controls throughput, backpressure, and initialization behavior.
        /// </summary>
        public PerformanceOptions Performance { get; set; } = new();

        /// <summary>
        /// Resilience configuration: Controls retry policies and circuit breaker behavior.
        /// </summary>
        public ResilienceOptions Resilience { get; set; } = new();
    }

    /// <summary>
    /// Configuration for updateable notification behavior.
    /// Defines how to extract the update key and optional sequence number from a notification.
    /// </summary>
    public class UpdateableNotificationConfig
    {
        /// <summary>
        /// Function to extract update key from notification entity.
        /// Return null/empty to disable updateable behavior for a specific instance.
        /// Example: For OrderStatusNotification, extract OrderId to update the same order's status.
        /// </summary>
        public Func<INotificationEntity, string?> UpdateKeyExtractor { get; set; } = null!;

        /// <summary>
        /// Optional function to extract sequence number for ordering.
        /// Useful when you need to ensure updates are applied in order.
        /// Example: For OrderStatusNotification, extract Sequence to ensure state transitions are ordered.
        /// </summary>
        public Func<INotificationEntity, long?>? SequenceExtractor { get; set; }

        /// <summary>
        /// Validates the configuration.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when UpdateKeyExtractor is null.</exception>
        public void Validate()
        {
            if (UpdateKeyExtractor == null)
                throw new ArgumentNullException(nameof(UpdateKeyExtractor), "UpdateKeyExtractor is required for updateable notifications.");
        }
    }
}
