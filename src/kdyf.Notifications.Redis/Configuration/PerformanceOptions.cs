using System.Threading.Channels;

namespace kdyf.Notifications.Redis.Configuration
{
    /// <summary>
    /// Configuration options for performance tuning.
    /// Controls fire-and-forget channels, backpressure, and initialization behavior.
    /// </summary>
    public class PerformanceOptions
    {
        /// <summary>
        /// Capacity of the bounded channel for fire-and-forget emissions.
        /// Determines how many notifications can be queued in memory before backpressure is applied.
        ///
        /// Recommended values:
        /// - 1000: Low-volume applications (&lt;100 notifications/sec)
        /// - 10000: Medium-volume applications (100-1000 notifications/sec) [DEFAULT]
        /// - 100000: High-volume or burst handling (1000+ notifications/sec)
        ///
        /// Default: 10000
        /// </summary>
        public int ChannelCapacity { get; set; } = 10000;

        /// <summary>
        /// Behavior when the channel reaches full capacity.
        ///
        /// Options:
        /// - Wait: Block until space is available (default, safest - ensures no data loss)
        /// - DropNewest: Discard the new notification (useful for non-critical metrics)
        /// - DropOldest: Discard the oldest notification (WARNING: guaranteed data loss)
        ///
        /// Default: Wait (recommended for production to prevent data loss)
        /// </summary>
        public BoundedChannelFullMode ChannelFullMode { get; set; } = BoundedChannelFullMode.Wait;

        /// <summary>
        /// Timeout in milliseconds for Redis initialization operations (creating streams, consumer groups).
        /// Initialization operations may take longer than normal operations, especially on slow networks.
        /// Default: 30000ms (30 seconds)
        /// </summary>
        public int InitializationTimeoutMs { get; set; } = 30000;

        /// <summary>
        /// Duration in milliseconds that XREADGROUP blocks waiting for new messages.
        /// This is NOT a timeout - it's how long Redis waits before returning empty result.
        ///
        /// Recommended values:
        /// - 1000ms (1s): Low-latency scenarios, frequent polling
        /// - 5000ms (5s): Balanced approach [DEFAULT]
        /// - 10000ms (10s): High-latency networks, reduce Redis load
        ///
        /// Lower values = More responsive but more Redis round-trips
        /// Higher values = Less responsive but fewer Redis round-trips
        ///
        /// Default: 5000ms (5 seconds)
        /// </summary>
        public int XReadGroupBlockMs { get; set; } = 5000;
    }
}
