using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace kdyf.Notifications.Redis.HealthChecks
{
    /// <summary>
    /// Health check for Redis notification infrastructure.
    /// Monitors Redis connection and stream availability.
    /// </summary>
    internal class RedisNotificationHealthCheck : IHealthCheck
    {
        private readonly IConnectionMultiplexer _redis;
        private readonly ILogger<RedisNotificationHealthCheck> _logger;
        private readonly string _streamName;

        /// <summary>
        /// Creates a new instance of the Redis notification health check.
        /// </summary>
        /// <param name="redis">Redis connection multiplexer.</param>
        /// <param name="logger">Logger for diagnostic messages.</param>
        /// <param name="streamName">Redis stream name to check.</param>
        public RedisNotificationHealthCheck(
            IConnectionMultiplexer redis,
            ILogger<RedisNotificationHealthCheck> logger,
            string streamName)
        {
            _redis = redis ?? throw new ArgumentNullException(nameof(redis));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _streamName = streamName ?? throw new ArgumentNullException(nameof(streamName));
        }

        /// <summary>
        /// Checks the health of the Redis notification infrastructure.
        /// </summary>
        /// <param name="context">Health check context.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Health check result indicating Redis connection status.</returns>
        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // Check if Redis is connected
                if (!_redis.IsConnected)
                {
                    return HealthCheckResult.Unhealthy(
                        "Redis connection is not established",
                        data: new Dictionary<string, object>
                        {
                            ["StreamName"] = _streamName,
                            ["IsConnected"] = false
                        });
                }

                var db = _redis.GetDatabase();

                // Try a simple PING to verify connection
                var pingResult = await db.PingAsync();

                // Check if stream exists
                var streamExists = await db.KeyExistsAsync(_streamName);

                var data = new Dictionary<string, object>
                {
                    ["StreamName"] = _streamName,
                    ["StreamExists"] = streamExists,
                    ["IsConnected"] = true,
                    ["PingLatency"] = pingResult.TotalMilliseconds
                };

                if (pingResult.TotalMilliseconds > 1000) // If ping takes more than 1 second
                {
                    return HealthCheckResult.Degraded(
                        $"Redis connection is slow (ping: {pingResult.TotalMilliseconds}ms)",
                        data: data);
                }

                return HealthCheckResult.Healthy(
                    $"Redis notification infrastructure is operational (ping: {pingResult.TotalMilliseconds}ms)",
                    data: data);
            }
            catch (RedisConnectionException ex)
            {
                _logger.LogError(ex, "Redis health check failed - connection error");
                return HealthCheckResult.Unhealthy(
                    "Redis connection failed",
                    ex,
                    data: new Dictionary<string, object>
                    {
                        ["StreamName"] = _streamName,
                        ["IsConnected"] = false
                    });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Redis health check encountered an error");
                return HealthCheckResult.Unhealthy(
                    "Redis health check encountered an error",
                    ex);
            }
        }
    }
}
