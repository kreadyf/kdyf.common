namespace kdyf.Notifications.Redis.Configuration
{
    /// <summary>
    /// Configuration options for resilience and fault tolerance.
    /// Controls retry policies and circuit breaker behavior for Redis operations.
    /// </summary>
    public class ResilienceOptions
    {
        /// <summary>
        /// Delay in milliseconds before retrying failed Redis key-value operations.
        /// Used when Redis connection fails during StringSetAsync operations.
        /// Default: 2000ms (2 seconds)
        /// </summary>
        public int RetryDelayMs { get; set; } = 2000;

        /// <summary>
        /// Delay in milliseconds before retrying after a Redis connection error in the receiver.
        /// Applied when XREADGROUP or other stream read operations fail due to connection issues.
        ///
        /// Recommended values:
        /// - 500ms: Fast recovery for transient issues
        /// - 1000ms: Balanced approach [DEFAULT]
        /// - 2000ms: Conservative approach for unstable connections
        ///
        /// Default: 1000ms (1 second)
        /// </summary>
        public int ErrorRecoveryDelayMs { get; set; } = 1000;
    }
}
