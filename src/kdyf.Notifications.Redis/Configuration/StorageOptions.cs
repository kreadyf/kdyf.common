namespace kdyf.Notifications.Redis.Configuration
{
    /// <summary>
    /// Configuration options for notification storage behavior.
    /// Controls how notifications are stored in Redis (streams, keys, TTL).
    /// </summary>
    public class StorageOptions
    {
        /// <summary>
        /// Default stream name to use when a type has no explicit mapping.
        /// This must be a FULL stream name (e.g., "notifications:stream:default"), NOT a suffix.
        /// Can be overridden from appsettings.json: "Redis:DefaultStreamName"
        /// Default: "notifications:stream:default"
        /// </summary>
        public string DefaultStreamName { get; set; } = "notifications:stream:default";

        /// <summary>
        /// Default time-to-live for notification message keys (key-value storage).
        /// When set, notification keys will be automatically deleted by Redis after this period.
        /// This controls how long notification payloads remain accessible via their key.
        /// Default: 1 hour (3600 seconds). Set to TimeSpan.Zero to disable key expiration.
        /// </summary>
        public TimeSpan MessageTTL { get; set; } = TimeSpan.FromHours(1);

        /// <summary>
        /// Default time-to-live for Redis stream keys.
        /// When set, streams will be automatically deleted by Redis after this period of inactivity.
        /// TTL is extended (reset) every time a message is added to the stream.
        /// This prevents infinite accumulation of streams in Redis.
        /// Default: 24 hours. Set to TimeSpan.Zero to disable stream expiration.
        /// </summary>
        public TimeSpan StreamTTL { get; set; } = TimeSpan.FromHours(24);

        /// <summary>
        /// Maximum length for Redis streams. When exceeded, older entries are automatically trimmed.
        /// Uses approximate trimming (~) for O(1) amortized performance.
        /// Default: 10000 entries. Set to 0 to disable trimming.
        /// </summary>
        public int MaxStreamLength { get; set; } = 10000;

        /// <summary>
        /// Whether to use approximate trimming (MAXLEN ~) for better performance.
        /// When true, Redis may keep slightly more entries than MaxStreamLength for efficiency.
        /// Default: false (exact trimming). Note: Approximate trimming may not work correctly with older Redis versions.
        /// </summary>
        public bool UseApproximateTrimming { get; set; } = false;
    }
}
