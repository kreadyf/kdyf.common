using kdyf.Notifications.Interfaces;
using kdyf.Notifications.Redis.Configuration;
using kdyf.Notifications.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace kdyf.Notifications.Redis.Services
{
    /// <summary>
    /// Factory for creating Redis notification receivers.
    /// Encapsulates all Redis-specific receiver creation logic,
    /// keeping the core notification project free of Redis dependencies.
    /// </summary>
    public class RedisReceiverFactory : INotificationReceiverFactory
    {
        /// <summary>
        /// Property key used to store Redis stream names in builder properties.
        /// </summary>
        public const string StreamNamesPropertyKey = "Redis.StreamNames";

        /// <summary>
        /// Creates Redis notification receiver instances based on the builder properties.
        /// Looks for "Redis.StreamNames" in properties - returns empty if not configured.
        /// </summary>
        /// <param name="serviceProvider">The service provider for resolving dependencies.</param>
        /// <param name="properties">The builder properties containing "Redis.StreamNames" configuration.</param>
        /// <returns>A collection of Redis notification receivers, one per stream, or empty if Redis not configured.</returns>
        public IEnumerable<INotificationReceiver> CreateReceivers(
            IServiceProvider serviceProvider,
            IDictionary<string, object> properties)
        {
            // Check if Redis is configured - if not, return empty (another factory handles this)
            if (!properties.ContainsKey(StreamNamesPropertyKey))
            {
                return Enumerable.Empty<INotificationReceiver>();
            }

            var streamNames = (List<string>)properties[StreamNamesPropertyKey];
            var receivers = new List<INotificationReceiver>();

            // Resolve dependencies directly (no reflection needed)
            var redis = serviceProvider.GetRequiredService<IConnectionMultiplexer>();
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();
            var logger = serviceProvider.GetRequiredService<ILogger<RedisNotificationReceiver>>();
            var typeResolver = serviceProvider.GetRequiredService<NotificationTypeResolver>();
            var streamParser = serviceProvider.GetRequiredService<RedisStreamParser>();
            var streamInitializer = serviceProvider.GetRequiredService<RedisStreamInitializer>();
            var redisOptions = serviceProvider.GetService<RedisNotificationOptions>();

            // Get default stream name from options
            var defaultStreamName = redisOptions?.Storage.DefaultStreamName
                ?? "notifications:stream:default";

            // Create one receiver per stream
            foreach (var streamNameOrNull in streamNames)
            {
                var streamName = string.IsNullOrEmpty(streamNameOrNull)
                    ? defaultStreamName
                    : streamNameOrNull;

                var receiver = new RedisNotificationReceiver(
                    redis,
                    configuration,
                    logger,
                    typeResolver,
                    streamParser,
                    streamInitializer,
                    streamName,
                    redisOptions);

                receivers.Add(receiver);
            }

            return receivers;
        }
    }
}
