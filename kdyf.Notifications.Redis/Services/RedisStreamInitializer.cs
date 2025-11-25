using kdyf.Notifications.Redis.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace kdyf.Notifications.Redis.Services
{
    /// <summary>
    /// Service responsible for initializing Redis streams and consumer groups.
    /// Provides retry logic, health checks, and error handling for initialization operations.
    /// Separates initialization logic from both HostedService and Receiver concerns.
    /// </summary>
    public class RedisStreamInitializer
    {
        private readonly ILogger<RedisStreamInitializer> _logger;
        private readonly RedisNotificationOptions? _options;

        /// <summary>
        /// Creates a new instance of the Redis stream initializer.
        /// </summary>
        /// <param name="logger">Logger for diagnostic messages.</param>
        /// <param name="options">Optional Redis notification options for retry and timeout configuration.</param>
        public RedisStreamInitializer(
            ILogger<RedisStreamInitializer> logger,
            RedisNotificationOptions? options = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _options = options;
        }

        /// <summary>
        /// Ensures a consumer group exists on a stream with simple retry logic.
        /// </summary>
        /// <param name="db">Redis database instance.</param>
        /// <param name="streamName">Name of the stream.</param>
        /// <param name="consumerGroup">Name of the consumer group.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <exception cref="InvalidOperationException">Thrown when initialization fails after all retries.</exception>
        public async Task EnsureConsumerGroupAsync(
            IDatabase db,
            string streamName,
            string consumerGroup,
            CancellationToken ct)
        {
            const int maxRetries = 5;
            const int retryDelayMs = 2000;
            var initTimeoutMs = _options?.Performance.InitializationTimeoutMs ?? 30000;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    // Create timeout token for initialization operations
                    using var timeoutCts = new CancellationTokenSource(initTimeoutMs);
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

                    // Check if stream exists, create if not
                    var streamExists = await db.KeyExistsAsync(streamName);
                    if (!streamExists)
                    {
                        _logger.LogInformation("Stream does not exist, creating: {StreamName}", streamName);
                        await db.StreamAddAsync(streamName, "init", "true");
                        _logger.LogInformation("Created stream: {StreamName}", streamName);
                    }

                    // Try to create consumer group
                    try
                    {
                        await db.StreamCreateConsumerGroupAsync(streamName, consumerGroup, StreamPosition.NewMessages);
                        _logger.LogInformation(
                            "Successfully created consumer group '{ConsumerGroup}' on stream '{StreamName}' (attempt {Attempt})",
                            consumerGroup, streamName, attempt);
                        return; // Success!
                    }
                    catch (RedisServerException ex) when (ex.Message.Contains("BUSYGROUP"))
                    {
                        _logger.LogDebug(
                            "Consumer group '{ConsumerGroup}' already exists on stream '{StreamName}'",
                            consumerGroup, streamName);
                        return; // Group already exists, that's fine
                    }
                }
                catch (RedisTimeoutException ex) when (attempt < maxRetries)
                {
                    var delayMs = retryDelayMs * attempt; // Exponential backoff
                    _logger.LogWarning(ex,
                        "Attempt {Attempt}/{MaxRetries}: Redis timeout while initializing stream '{StreamName}'. " +
                        "Retrying in {Delay}ms... (Timeout was: {Timeout}ms)",
                        attempt, maxRetries, streamName, delayMs, initTimeoutMs);

                    await Task.Delay(delayMs, ct);
                }
                catch (RedisConnectionException ex) when (attempt < maxRetries)
                {
                    var delayMs = retryDelayMs * attempt;
                    _logger.LogWarning(ex,
                        "Attempt {Attempt}/{MaxRetries}: Redis connection error while initializing stream '{StreamName}'. " +
                        "Retrying in {Delay}ms...",
                        attempt, maxRetries, streamName, delayMs);

                    await Task.Delay(delayMs, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    _logger.LogInformation(
                        "Consumer group initialization cancelled for stream '{StreamName}'",
                        streamName);
                    throw;
                }
                catch (Exception ex) when (attempt < maxRetries)
                {
                    var delayMs = retryDelayMs * attempt;
                    _logger.LogWarning(ex,
                        "Attempt {Attempt}/{MaxRetries}: Unexpected error while initializing stream '{StreamName}'. " +
                        "Retrying in {Delay}ms...",
                        attempt, maxRetries, streamName, delayMs);

                    await Task.Delay(delayMs, ct);
                }
            }

            // All retries exhausted - always fail fast
            var errorMsg = $"Failed to initialize consumer group '{consumerGroup}' on stream '{streamName}' after {maxRetries} attempts.";
            _logger.LogCritical("{ErrorMessage} Application startup will fail.", errorMsg);
            throw new InvalidOperationException(errorMsg);
        }

        /// <summary>
        /// Sets TTL on a stream to prevent infinite accumulation.
        /// </summary>
        /// <param name="db">Redis database instance.</param>
        /// <param name="streamName">Name of the stream.</param>
        /// <param name="ttl">TTL duration.</param>
        public async Task SetStreamTTLAsync(IDatabase db, string streamName, TimeSpan ttl)
        {
            if (ttl > TimeSpan.Zero)
            {
                await db.KeyExpireAsync(streamName, ttl);
                _logger.LogInformation("Set stream TTL: {StreamName} → {TTL}", streamName, ttl);
            }
        }
    }
}
