using kdyf.Notifications.Integration;
using kdyf.Notifications.Interfaces;
using kdyf.Notifications.Redis.Configuration;

namespace kdyf.Notifications.Redis.Integration
{
    /// <summary>
    /// Fluent configuration for Redis notification emitter.
    /// Defines type-to-stream routing and storage strategies.
    /// </summary>
    public class RedisEmitterConfiguration
    {
        internal Dictionary<Type, string> TypeToStreamMapping { get; } = new();
        internal HashSet<Type> StreamOnlyTypes { get; } = new();
        internal string? DefaultStreamName { get; private set; }

        /// <summary>
        /// Sets the default stream name for all notification types that are not explicitly configured.
        /// This stream will be used when a notification type has no explicit mapping in TypeToStreamMapping.
        /// IMPORTANT: streamName must be a FULL stream name, not a suffix.
        /// </summary>
        /// <param name="streamName">The FULL default stream name (e.g., "notifications:stream:default").</param>
        /// <returns>The configuration instance for chaining.</returns>
        /// <example>
        /// <code>
        /// .AddRedisTarget(cfg => cfg
        ///     .WithStream("notifications:stream:default")  // Default for unmapped types
        ///     .WithStream&lt;OrderNotification&gt;("notifications:stream:orders")  // Specific mapping
        ///     .WithStream&lt;PaymentNotification&gt;("notifications:stream:payments"))  // Specific mapping
        /// </code>
        /// </example>
        public RedisEmitterConfiguration WithStream(string streamName)
        {
            if (string.IsNullOrWhiteSpace(streamName))
                throw new ArgumentException("Stream name cannot be null or empty. Must be a full stream name.", nameof(streamName));

            DefaultStreamName = streamName;
            return this;
        }

        /// <summary>
        /// Configures a notification type to be emitted to a specific stream.
        /// IMPORTANT: streamName must be a FULL stream name, not a suffix.
        /// All streams must be configured explicitly at startup - no dynamic creation.
        /// </summary>
        /// <typeparam name="T">The notification entity type.</typeparam>
        /// <param name="streamName">The FULL stream name (e.g., "notifications:stream:orders").</param>
        /// <returns>The configuration instance for chaining.</returns>
        /// <example>
        /// <code>
        /// .AddRedisTarget(cfg => cfg
        ///     .WithStream&lt;OrderNotification&gt;("notifications:stream:orders")
        ///     .WithStream&lt;PaymentNotification&gt;("notifications:stream:payments"))
        /// </code>
        /// </example>
        public RedisEmitterConfiguration WithStream<T>(string streamName)
            where T : INotificationEntity
        {
            if (string.IsNullOrWhiteSpace(streamName))
                throw new ArgumentException("Stream name cannot be null or empty. Must be a full stream name.", nameof(streamName));

            TypeToStreamMapping[typeof(T)] = streamName;
            return this;
        }

        /// <summary>
        /// Configures a notification type as stream-only (no key-value storage)
        /// and optionally routes it to a specific stream.
        /// Combines storage optimization with stream routing.
        /// IMPORTANT: streamName must be a FULL stream name if provided, not a suffix.
        /// </summary>
        /// <typeparam name="T">The notification entity type.</typeparam>
        /// <param name="streamName">Optional FULL stream name for routing. If null, uses DefaultStreamName.</param>
        /// <returns>The configuration instance for chaining.</returns>
        /// <example>
        /// <code>
        /// .AddRedisTarget(cfg => cfg
        ///     .WithStreamOnly&lt;MetricNotification&gt;("notifications:stream:metrics")
        ///     .WithStreamOnly&lt;LogNotification&gt;()) // Uses DefaultStreamName
        /// </code>
        /// </example>
        public RedisEmitterConfiguration WithStreamOnly<T>(string? streamName = null)
            where T : INotificationEntity
        {
            StreamOnlyTypes.Add(typeof(T));
            if (!string.IsNullOrWhiteSpace(streamName))
                TypeToStreamMapping[typeof(T)] = streamName;
            return this;
        }

    }

    /// <summary>
    /// Fluent configuration for Redis notification receiver.
    /// Defines which streams this service should consume.
    /// </summary>
    public class RedisReceiverConfiguration
    {
        internal List<string> StreamNames { get; } = new();

        /// <summary>
        /// Specifies the streams this receiver should consume.
        /// Creates one receiver instance per stream for parallel consumption.
        /// IMPORTANT: streamNames must be FULL stream names, not suffixes.
        /// </summary>
        /// <param name="streamNames">Full stream names to consume (e.g., "notifications:stream:orders", "notifications:stream:payments").</param>
        /// <returns>The configuration instance for chaining.</returns>
        /// <example>
        /// <code>
        /// // Service that only consumes orders and payments
        /// .AddRedisSource(cfg => cfg.WithStreams("notifications:stream:orders", "notifications:stream:payments"))
        ///
        /// // Service that consumes default stream
        /// .AddRedisSource()
        /// </code>
        /// </example>
        public RedisReceiverConfiguration WithStreams(params string[] streamNames)
        {
            if (streamNames == null || streamNames.Length == 0)
                throw new ArgumentException("At least one stream name must be provided.", nameof(streamNames));

            foreach (var streamName in streamNames)
            {
                if (string.IsNullOrWhiteSpace(streamName))
                    throw new ArgumentException("Stream names cannot be null or empty. Must be full stream names.", nameof(streamNames));
            }

            StreamNames.AddRange(streamNames);
            return this;
        }
    }

    /// <summary>
    /// Extension methods for configuring Redis-specific notification features.
    /// Provides infrastructure-only configuration for updateable messages, stream-only storage, and multi-stream routing.
    /// </summary>
    public static class RedisNotificationBuilderExtensions
    {
        private const string RedisOptionsKey = "kdyf.Notifications.Redis.Options";

        /// <summary>
        /// Configures a notification type as updateable with a custom update key.
        /// When configured, notifications will UPDATE the same Redis key instead of creating new ones.
        /// This is useful for scenarios where you send multiple updates of the same logical entity
        /// (e.g., order status updates, price changes, location updates).
        /// </summary>
        /// <typeparam name="T">The notification entity type to configure.</typeparam>
        /// <param name="builder">The notification builder.</param>
        /// <param name="updateKeyExtractor">Function to extract the update key from the notification.
        /// Return null or empty to use standard (non-updateable) behavior for that instance.</param>
        /// <param name="sequenceExtractor">Optional function to extract a sequence number for ordering updates.</param>
        /// <returns>The notification builder for chaining.</returns>
        /// <example>
        /// <code>
        /// services.AddKdyfNotification(configuration)
        ///     .AddRedisTarget()
        ///     .ConfigureUpdateable&lt;OrderStatusNotification&gt;(
        ///         updateKeyExtractor: n => n.OrderId,
        ///         sequenceExtractor: n => n.Sequence
        ///     )
        ///     .Build();
        /// </code>
        /// </example>
        public static INotificationBuilder ConfigureUpdateable<T>(
            this INotificationBuilder builder,
            Func<T, string?> updateKeyExtractor,
            Func<T, long?>? sequenceExtractor = null)
            where T : class, INotificationEntity
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));
            if (updateKeyExtractor == null)
                throw new ArgumentNullException(nameof(updateKeyExtractor));

            var options = GetOrCreateRedisOptions(builder);

            var config = new UpdateableNotificationConfig
            {
                UpdateKeyExtractor = entity => updateKeyExtractor((T)entity),
                SequenceExtractor = sequenceExtractor != null
                    ? entity => sequenceExtractor((T)entity)
                    : null
            };

            config.Validate();

            options.UpdateableTypes[typeof(T)] = config;

            return builder;
        }

        /// <summary>
        /// Configures a notification type to store full payload in Redis stream only (no separate key-value storage).
        /// This reduces Redis operations from 2 (SET + XADD) to 1 (XADD only) per notification.
        /// Useful for high-frequency, small payload notifications where reducing operations is important.
        /// </summary>
        /// <typeparam name="T">The notification entity type to configure.</typeparam>
        /// <param name="builder">The notification builder.</param>
        /// <returns>The notification builder for chaining.</returns>
        /// <example>
        /// <code>
        /// services.AddKdyfNotification(configuration)
        ///     .AddRedisTarget()
        ///     .ConfigureStreamOnly&lt;HighFrequencyMetricNotification&gt;()
        ///     .Build();
        /// </code>
        /// </example>
        public static INotificationBuilder ConfigureStreamOnly<T>(this INotificationBuilder builder)
            where T : class, INotificationEntity
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));

            var options = GetOrCreateRedisOptions(builder);
            options.StreamOnlyTypes.Add(typeof(T));

            return builder;
        }

        /// <summary>
        /// Configures multiple options for Redis notifications at once.
        /// </summary>
        /// <param name="builder">The notification builder.</param>
        /// <param name="configure">Action to configure Redis notification options.</param>
        /// <returns>The notification builder for chaining.</returns>
        public static INotificationBuilder ConfigureRedisOptions(
            this INotificationBuilder builder,
            Action<RedisNotificationOptions> configure)
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));
            if (configure == null)
                throw new ArgumentNullException(nameof(configure));

            var options = GetOrCreateRedisOptions(builder);
            configure(options);

            return builder;
        }

        /// <summary>
        /// Gets or creates the Redis notification options from the builder properties.
        /// </summary>
        /// <param name="builder">The notification builder.</param>
        /// <returns>The Redis notification options instance.</returns>
        internal static RedisNotificationOptions GetOrCreateRedisOptions(INotificationBuilder builder)
        {
            if (!builder.Properties.TryGetValue(RedisOptionsKey, out var value))
            {
                value = new RedisNotificationOptions();
                builder.Properties[RedisOptionsKey] = value;
            }

            return (RedisNotificationOptions)value;
        }

        /// <summary>
        /// Gets the Redis notification options if configured, otherwise returns null.
        /// </summary>
        /// <param name="builder">The notification builder.</param>
        /// <returns>The Redis notification options or null if not configured.</returns>
        public static RedisNotificationOptions? GetRedisOptions(this INotificationBuilder builder)
        {
            if (builder.Properties.TryGetValue(RedisOptionsKey, out var value))
            {
                return (RedisNotificationOptions)value;
            }
            return null;
        }
    }
}
