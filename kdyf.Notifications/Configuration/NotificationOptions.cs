using Microsoft.Extensions.Caching.Memory;

namespace kdyf.Notifications.Configuration
{
    /// <summary>
    /// Configuration options for the notification system.
    /// Controls deduplication cache behavior and other system-wide settings.
    /// </summary>
    public class NotificationOptions
    {
        /// <summary>
        /// Time-to-live for deduplication cache entries.
        /// After this period, duplicate notifications will be allowed.
        /// Default: 10 minutes.
        /// </summary>
        public TimeSpan DeduplicationTtl { get; set; } = TimeSpan.FromMinutes(10);

        /// <summary>
        /// Maximum number of entries in the deduplication cache.
        /// When this limit is reached, least recently used entries will be evicted.
        /// Default: 10,000 entries.
        /// At 5-10 notifications/sec, this allows ~16-33 minutes of unique notifications.
        /// </summary>
        public int MaxDeduplicationCacheSize { get; set; } = 10_000;

        /// <summary>
        /// Percentage of cache to compact when size limit is reached.
        /// Default: 0.25 (25%) - when cache is full, remove 25% of oldest entries.
        /// </summary>
        public double CacheCompactionPercentage { get; set; } = 0.25;

        /// <summary>
        /// Interval for cache size scanning when approaching limit.
        /// Default: 1 minute.
        /// </summary>
        public TimeSpan CacheScanInterval { get; set; } = TimeSpan.FromMinutes(1);

        /// <summary>
        /// Creates MemoryCacheOptions configured with these settings.
        /// </summary>
        /// <returns>Configured MemoryCacheOptions instance.</returns>
        public MemoryCacheOptions ToMemoryCacheOptions()
        {
            return new MemoryCacheOptions
            {
                SizeLimit = MaxDeduplicationCacheSize,
                CompactionPercentage = CacheCompactionPercentage,
                ExpirationScanFrequency = CacheScanInterval
            };
        }

        /// <summary>
        /// Validates the configuration values.
        /// </summary>
        /// <exception cref="ArgumentException">Thrown when configuration values are invalid.</exception>
        public void Validate()
        {
            if (DeduplicationTtl <= TimeSpan.Zero)
                throw new ArgumentException("DeduplicationTtl must be greater than zero.", nameof(DeduplicationTtl));

            if (MaxDeduplicationCacheSize <= 0)
                throw new ArgumentException("MaxDeduplicationCacheSize must be greater than zero.", nameof(MaxDeduplicationCacheSize));

            if (CacheCompactionPercentage <= 0 || CacheCompactionPercentage >= 1)
                throw new ArgumentException("CacheCompactionPercentage must be between 0 and 1 (exclusive).", nameof(CacheCompactionPercentage));

            if (CacheScanInterval <= TimeSpan.Zero)
                throw new ArgumentException("CacheScanInterval must be greater than zero.", nameof(CacheScanInterval));
        }
    }
}
