using kdyf.Notifications.Integration;
using kdyf.Notifications.Redis.Configuration;
using kdyf.Notifications.Redis.Services;
using kdyf.Notifications.Redis.HealthChecks;
using kdyf.Notifications.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace kdyf.Notifications.Redis.Integration
{
    /// <summary>
    /// Extension methods for configuring Redis notification services.
    /// Uses reactive IObservable pattern for receivers (no BackgroundService re-emission).
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Adds Redis as a notification source using reactive IObservable pattern.
        /// Optionally configures which streams to consume for selective consumption.
        /// Creates one receiver instance per stream for parallel consumption.
        /// </summary>
        /// <param name="builder">The notification builder to configure.</param>
        /// <param name="configure">Optional action to configure which streams to consume.</param>
        /// <returns>The notification builder for chaining.</returns>
        /// <example>
        /// <code>
        /// // Consume specific streams
        /// .AddRedisSource(cfg => cfg.WithStreams("orders", "payments"))
        ///
        /// // Consume default stream (backward compatible)
        /// .AddRedisSource()
        /// </code>
        /// </example>
        public static INotificationBuilder AddRedisSource(
            this INotificationBuilder builder,
            Action<RedisReceiverConfiguration>? configure = null)
        {
            // Apply fluent configuration if provided
            List<string> streamNames;
            if (configure != null)
            {
                var config = new RedisReceiverConfiguration();
                configure(config);
                streamNames = config.StreamNames;
            }
            else
            {
                // Default: consume from default stream (use DefaultStreamName from options)
                // Will be resolved at receiver creation time
                streamNames = new List<string> { null! }; // null indicates use DefaultStreamName
            }

            // Store stream names in builder properties for later use by composite builder
            if (!builder.Properties.ContainsKey("Redis.StreamNames"))
            {
                builder.Properties["Redis.StreamNames"] = new List<string>();
            }
            var storedStreamNames = (List<string>)builder.Properties["Redis.StreamNames"];
            storedStreamNames.AddRange(streamNames);

            // Register Redis stream parser for RESP2 protocol parsing
            if (!builder.Services.Any(sd => sd.ServiceType == typeof(RedisStreamParser)))
            {
                builder.Services.AddSingleton<RedisStreamParser>();
            }

            // Register Redis stream initializer for reusable stream/consumer group initialization logic
            if (!builder.Services.Any(sd => sd.ServiceType == typeof(RedisStreamInitializer)))
            {
                builder.Services.AddSingleton<RedisStreamInitializer>(sp =>
                {
                    var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<RedisStreamInitializer>>();
                    var redisOptions = sp.GetService<RedisNotificationOptions>();
                    return new RedisStreamInitializer(logger, redisOptions);
                });
            }


            // Register ONE instance for direct DI resolution (for tests that need it)
            if (!builder.Services.Any(sd => sd.ServiceType == typeof(RedisNotificationReceiver)))
            {
                builder.Services.AddSingleton<RedisNotificationReceiver>(sp =>
                {
                    var redis = sp.GetRequiredService<IConnectionMultiplexer>();
                    var configuration = sp.GetRequiredService<IConfiguration>();
                    var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<RedisNotificationReceiver>>();
                    var typeResolver = sp.GetRequiredService<NotificationTypeResolver>();
                    var streamParser = sp.GetRequiredService<RedisStreamParser>();
                    var streamInitializer = sp.GetRequiredService<RedisStreamInitializer>();
                    var redisOptions = sp.GetService<RedisNotificationOptions>();

                    return new RedisNotificationReceiver(redis, configuration, logger, typeResolver, streamParser, streamInitializer, null, redisOptions);
                });
            }

            // Track the receiver type for composite creation
            if (!builder.Receivers.Contains(typeof(RedisNotificationReceiver)))
            {
                builder.Receivers.Add(typeof(RedisNotificationReceiver));
            }

            return builder;
        }

        /// <summary>
        /// Adds Redis as a notification target by registering the Redis notification emitter.
        /// Configures Redis connection multiplexer from application configuration.
        /// Optionally configures type-to-stream routing and storage strategies.
        /// </summary>
        /// <param name="builder">The notification builder to configure.</param>
        /// <param name="configure">Optional action to configure emitter settings (routing, stream-only types).</param>
        /// <returns>The notification builder for chaining.</returns>
        /// <exception cref="InvalidOperationException">Thrown when Redis connection string is not configured.</exception>
        /// <example>
        /// <code>
        /// // With routing configuration
        /// .AddRedisTarget(cfg => cfg
        ///     .WithStream&lt;OrderNotification&gt;("orders")
        ///     .WithStreamOnly&lt;MetricNotification&gt;("metrics"))
        ///
        /// // Without configuration (backward compatible)
        /// .AddRedisTarget()
        /// </code>
        /// </example>
        public static INotificationBuilder AddRedisTarget(
            this INotificationBuilder builder,
            Action<RedisEmitterConfiguration>? configure = null)
        {
            // Apply fluent configuration if provided
            if (configure != null)
            {
                var config = new RedisEmitterConfiguration();
                configure(config);

                var redisOptions = RedisNotificationBuilderExtensions.GetOrCreateRedisOptions(builder);

                // Apply default stream name if configured
                if (!string.IsNullOrWhiteSpace(config.DefaultStreamName))
                {
                    redisOptions.Storage.DefaultStreamName = config.DefaultStreamName;
                }

                // Apply type-to-stream mappings
                foreach (var (type, streamName) in config.TypeToStreamMapping)
                {
                    redisOptions.TypeToStreamMapping[type] = streamName;
                }

                // Apply stream-only types
                foreach (var type in config.StreamOnlyTypes)
                {
                    redisOptions.StreamOnlyTypes.Add(type);
                }
            }


            // Configure Redis connection (shared by both emitter and receiver)
            if (!builder.Services.Any(sd => sd.ServiceType == typeof(IConnectionMultiplexer)))
            {
                builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
                {
                    var connectionString = builder.Configuration.GetSection("Redis")["ConnectionString"];

                    if (string.IsNullOrWhiteSpace(connectionString))
                        throw new InvalidOperationException("Redis:ConnectionString not configured in appsettings.json.");

                    var options = ConfigurationOptions.Parse(connectionString);
                    options.AbortOnConnectFail = false;

                    // Automatically ensure asyncTimeout and syncTimeout are sufficient for XReadGroupBlockMs
                    // This prevents RedisTimeoutException when XREADGROUP blocks for the full duration
                    var redisOptions = sp.GetService<RedisNotificationOptions>();
                    var xReadGroupBlockMs = redisOptions?.Performance.XReadGroupBlockMs ?? 5000;
                    
                    // Minimum timeout = XReadGroupBlockMs * 2.5 + 15 seconds buffer
                    // This ensures XREADGROUP never times out even with high network latency and processing delays
                    // For high-performance apps (40+ notifications/sec), XREADGROUP should return quickly,
                    // but we need generous timeout for edge cases (network hiccups, Redis load spikes, etc.)
                    const double timeoutMultiplier = 2.5;
                    const int safetyBufferMs = 15000;
                    var minimumRequiredTimeout = (int)(xReadGroupBlockMs * timeoutMultiplier) + safetyBufferMs;

                    // Get logger using factory to ensure it's always available
                    var loggerFactory = sp.GetService<Microsoft.Extensions.Logging.ILoggerFactory>();
                    var logger = loggerFactory?.CreateLogger("kdyf.Notifications.Redis.Connection");
                    bool timeoutAdjusted = false;

                    // Ensure asyncTimeout is sufficient (used by XREADGROUP and most async operations)
                    if (options.AsyncTimeout < minimumRequiredTimeout)
                    {
                        var originalAsyncTimeout = options.AsyncTimeout;
                        options.AsyncTimeout = minimumRequiredTimeout;
                        timeoutAdjusted = true;
                        logger?.LogWarning(
                            "Redis asyncTimeout automatically adjusted from {Original}ms to {Adjusted}ms " +
                            "to prevent timeouts with XReadGroupBlockMs={BlockMs}. " +
                            "Consider setting asyncTimeout={Adjusted}ms in your ConnectionString.",
                            originalAsyncTimeout, minimumRequiredTimeout, xReadGroupBlockMs, minimumRequiredTimeout);
                    }

                    // Ensure syncTimeout is sufficient (used by synchronous operations)
                    if (options.SyncTimeout < minimumRequiredTimeout)
                    {
                        var originalSyncTimeout = options.SyncTimeout;
                        options.SyncTimeout = minimumRequiredTimeout;
                        timeoutAdjusted = true;
                        logger?.LogWarning(
                            "Redis syncTimeout automatically adjusted from {Original}ms to {Adjusted}ms " +
                            "to prevent timeouts with XReadGroupBlockMs={BlockMs}. " +
                            "Consider setting syncTimeout={Adjusted}ms in your ConnectionString.",
                            originalSyncTimeout, minimumRequiredTimeout, xReadGroupBlockMs, minimumRequiredTimeout);
                    }

                    if (timeoutAdjusted && logger != null)
                    {
                        logger.LogInformation(
                            "Redis timeouts automatically configured: asyncTimeout={AsyncTimeout}ms, syncTimeout={SyncTimeout}ms " +
                            "(XReadGroupBlockMs={BlockMs}ms * {Multiplier} + {Buffer}ms safety buffer)",
                            options.AsyncTimeout, options.SyncTimeout, xReadGroupBlockMs, timeoutMultiplier, safetyBufferMs);
                    }

                    return ConnectionMultiplexer.Connect(options);
                });
            }

            // Register Retry Policy for transient failure handling
            builder.Services.AddSingleton<Resilience.IRetryPolicy>(sp =>
            {
                var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Resilience.SimpleRetryPolicy>>();
                var redisOptions = sp.GetService<RedisNotificationOptions>();

                var retryDelayMs = redisOptions?.Resilience.RetryDelayMs ?? 2000;

                return new Resilience.SimpleRetryPolicy(retryDelayMs, logger);
            });

            // Register RedisNotificationEmitter
            // RedisNotificationOptions will be registered by Build() if configured
            builder.Services.AddSingleton<RedisNotificationEmitter>(sp =>
            {
                var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<RedisNotificationEmitter>>();
                var redis = sp.GetRequiredService<IConnectionMultiplexer>();
                var configuration = sp.GetRequiredService<IConfiguration>();
                var retryPolicy = sp.GetRequiredService<Resilience.IRetryPolicy>();
                var redisOptions = sp.GetService<RedisNotificationOptions>();

                return new RedisNotificationEmitter(logger, redis, configuration, retryPolicy, redisOptions);
            });

            builder.Services.AddSingleton<IRedisNotificationEmitter>(sp =>
                sp.GetRequiredService<RedisNotificationEmitter>());

            // Register as IHostedService to start fire-and-forget background processor
            builder.Services.AddHostedService<RedisNotificationEmitter>(sp =>
                sp.GetRequiredService<RedisNotificationEmitter>());

            // Track the emitter type for composite creation
            if (!builder.Emitters.Contains(typeof(RedisNotificationEmitter)))
            {
                builder.Emitters.Add(typeof(RedisNotificationEmitter));
            }

            return builder;
        }

        /// <summary>
        /// Adds health checks for the Redis notification infrastructure.
        /// Monitors Redis connection and stream availability.
        /// </summary>
        /// <param name="builder">The notification builder to configure.</param>
        /// <param name="name">Optional name for the health check. Defaults to "redis-notifications".</param>
        /// <returns>The notification builder for chaining.</returns>
        public static INotificationBuilder AddRedisHealthChecks(this INotificationBuilder builder, string name = "redis-notifications")
        {
            // Use DefaultStreamName from options for health check
            builder.Services.AddHealthChecks()
                .AddCheck<RedisNotificationHealthCheck>(
                    name,
                    tags: new[] { "redis", "notifications", "ready" },
                    timeout: TimeSpan.FromSeconds(5));

            // Register health check dependencies - resolve stream name from options
            builder.Services.AddSingleton(sp =>
            {
                var redisOptions = sp.GetService<RedisNotificationOptions>();
                var streamName = redisOptions?.Storage.DefaultStreamName ?? "notifications:stream:default";
                
                return new RedisNotificationHealthCheck(
                    sp.GetRequiredService<IConnectionMultiplexer>(),
                    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<RedisNotificationHealthCheck>>(),
                    streamName);
            });

            return builder;
        }
    }
}
