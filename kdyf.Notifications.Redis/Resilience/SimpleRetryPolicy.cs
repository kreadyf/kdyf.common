using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace kdyf.Notifications.Redis.Resilience
{
    /// <summary>
    /// Simple retry policy for handling transient Redis connection failures.
    /// Implements a single retry attempt with configurable delay.
    /// For more advanced scenarios (exponential backoff, multiple retries), consider using Polly.
    /// </summary>
    public class SimpleRetryPolicy : IRetryPolicy
    {
        private readonly int _retryDelayMs;
        private readonly ILogger<SimpleRetryPolicy> _logger;

        /// <summary>
        /// Creates a new instance of the simple retry policy.
        /// </summary>
        /// <param name="retryDelayMs">Delay in milliseconds before retrying (default: 2000ms).</param>
        /// <param name="logger">Logger for diagnostic messages.</param>
        public SimpleRetryPolicy(
            int retryDelayMs,
            ILogger<SimpleRetryPolicy> logger)
        {
            _retryDelayMs = retryDelayMs;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Executes an operation with a single retry on RedisConnectionException.
        /// </summary>
        public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken = default)
        {
            if (operation == null)
                throw new ArgumentNullException(nameof(operation));

            try
            {
                return await operation();
            }
            catch (RedisConnectionException ex)
            {
                _logger.LogWarning(ex, "Redis connection failed, retrying after {DelayMs}ms...", _retryDelayMs);

                await Task.Delay(TimeSpan.FromMilliseconds(_retryDelayMs), cancellationToken);

                // Retry once
                return await operation();
            }
        }

        /// <summary>
        /// Executes an operation with a single retry on RedisConnectionException (void return).
        /// </summary>
        public async Task ExecuteAsync(Func<Task> operation, CancellationToken cancellationToken = default)
        {
            if (operation == null)
                throw new ArgumentNullException(nameof(operation));

            try
            {
                await operation();
            }
            catch (RedisConnectionException ex)
            {
                _logger.LogWarning(ex, "Redis connection failed, retrying after {DelayMs}ms...", _retryDelayMs);

                await Task.Delay(TimeSpan.FromMilliseconds(_retryDelayMs), cancellationToken);

                // Retry once
                await operation();
            }
        }
    }
}
