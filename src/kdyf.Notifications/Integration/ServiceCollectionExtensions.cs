using kdyf.Notifications.Integration;
using kdyf.Notifications.Interfaces;
using kdyf.Notifications.Services;
using kdyf.Notifications.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace kdyf.Notifications.Integration
{
    /// <summary>
    /// Extension methods for configuring notification services in the dependency injection container.
    /// Uses Composite Pattern to coordinate multiple notification transports with centralized deduplication.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Adds KDYF notification services to the service collection.
        /// Registers core infrastructure and InMemory transport by default.
        /// </summary>
        /// <param name="services">The service collection to configure.</param>
        /// <param name="configuration">The application configuration.</param>
        /// <returns>A notification builder for further configuration.</returns>
        public static INotificationBuilder AddKdyfNotification(this IServiceCollection services, IConfiguration configuration)
        {
            var builder = new DefaultNotificationBuilder(services, configuration);

            // Register core services
            services.TryAddSingleton<NotificationTypeResolver>();

            // Register InMemory transport by default (always present)
            builder.AddInMemoryTarget();
            builder.AddInMemorySource();

            return builder;
        }

        /// <summary>
        /// Adds InMemory emitter to the notification pipeline.
        /// InMemory transport does NOT deduplicate (that's CompositeReceiver's responsibility).
        /// </summary>
        /// <param name="builder">The notification builder to configure.</param>
        /// <returns>The notification builder for chaining.</returns>
        public static INotificationBuilder AddInMemoryTarget(this INotificationBuilder builder)
        {
            // Register shared Subject for InMemory transport (used by both emitter and receiver)
            builder.Services.TryAddSingleton<System.Reactive.Subjects.ISubject<INotificationEntity>>(sp =>
            {
                var subject = new System.Reactive.Subjects.Subject<INotificationEntity>();
                return System.Reactive.Subjects.Subject.Synchronize(subject);
            });

            // Register InMemoryNotificationEmitter as singleton
            builder.Services.TryAddSingleton<InMemoryNotificationEmitter>();

            // Track the emitter type for composite creation
            if (!builder.Emitters.Contains(typeof(InMemoryNotificationEmitter)))
            {
                builder.Emitters.Add(typeof(InMemoryNotificationEmitter));
            }

            return builder;
        }

        /// <summary>
        /// Adds InMemory receiver to the notification pipeline.
        /// InMemory transport does NOT deduplicate (that's CompositeReceiver's responsibility).
        /// </summary>
        /// <param name="builder">The notification builder to configure.</param>
        /// <returns>The notification builder for chaining.</returns>
        public static INotificationBuilder AddInMemorySource(this INotificationBuilder builder)
        {
            // Ensure shared Subject is registered (in case AddInMemoryTarget wasn't called)
            builder.Services.TryAddSingleton<System.Reactive.Subjects.ISubject<INotificationEntity>>(sp =>
            {
                var subject = new System.Reactive.Subjects.Subject<INotificationEntity>();
                return System.Reactive.Subjects.Subject.Synchronize(subject);
            });

            // Register InMemoryNotificationReceiver as singleton
            builder.Services.TryAddSingleton<InMemoryNotificationReceiver>();

            // Track the receiver type for composite creation
            if (!builder.Receivers.Contains(typeof(InMemoryNotificationReceiver)))
            {
                builder.Receivers.Add(typeof(InMemoryNotificationReceiver));
            }

            return builder;
        }

        /// <summary>
        /// Configures notification options such as cache size limits and TTL.
        /// </summary>
        /// <param name="builder">The notification builder to configure.</param>
        /// <param name="configure">Action to configure notification options.</param>
        /// <returns>The notification builder for chaining.</returns>
        public static INotificationBuilder ConfigureOptions(this INotificationBuilder builder, Action<NotificationOptions> configure)
        {
            configure?.Invoke(builder.Options);
            builder.Options.Validate(); // Validate after configuration
            return builder;
        }

        /// <summary>
        /// Builds and registers the composite notification services.
        /// Creates CompositeNotificationEmitter and CompositeNotificationReceiver
        /// that coordinate all registered transports with centralized deduplication.
        ///
        /// IMPORTANT: Requires ILogger and IMemoryCache to be registered.
        /// Tests should call services.AddLogging() before calling Build().
        /// </summary>
        /// <param name="builder">The notification builder to build.</param>
        /// <exception cref="InvalidOperationException">Thrown when configuration is invalid.</exception>
        public static void Build(this INotificationBuilder builder)
        {
            // VALIDATION: Ensure at least one emitter is registered
            if (builder.Emitters.Count == 0)
            {
                throw new InvalidOperationException(
                    "No notification emitters registered. At least one emitter (e.g., InMemory, Redis) must be configured.");
            }

            // VALIDATION: Ensure at least one receiver is registered
            if (builder.Receivers.Count == 0)
            {
                throw new InvalidOperationException(
                    "No notification receivers registered. At least one receiver (e.g., InMemory, Redis) must be configured.");
            }

            // VALIDATION: Check if Redis is configured and validate connection string
            if (builder.Emitters.Any(t => t.Name.Contains("Redis")) ||
                builder.Receivers.Any(t => t.Name.Contains("Redis")))
            {
                var redisConnectionString = builder.Configuration.GetSection("Redis")["ConnectionString"];
                if (string.IsNullOrWhiteSpace(redisConnectionString))
                {
                    throw new InvalidOperationException(
                        "Redis transport is registered but 'Redis:ConnectionString' is not configured in application configuration.");
                }
            }

            // STEP 1: Validate and register NotificationOptions
            builder.Options.Validate();
            builder.Services.AddSingleton(builder.Options);

            // STEP 1b: Register RedisNotificationOptions if configured (for updateable/stream-only features)
            // This uses reflection to avoid circular dependency between kdyf.Notifications and kdyf.Notifications.Redis
            if (builder.Properties.ContainsKey("kdyf.Notifications.Redis.Options"))
            {
                var redisOptions = builder.Properties["kdyf.Notifications.Redis.Options"];
                var optionsType = redisOptions.GetType();

                // Register as singleton using the actual type
                builder.Services.AddSingleton(optionsType, redisOptions);
            }

            // STEP 2: Register MemoryCache with configured size limits
            builder.Services.TryAddSingleton<IMemoryCache>(sp =>
            {
                var options = sp.GetRequiredService<NotificationOptions>();
                return new MemoryCache(options.ToMemoryCacheOptions());
            });

            // STEP 5: Register CompositeNotificationEmitter as the public INotificationEmitter
            builder.Services.AddSingleton<INotificationEmitter>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<CompositeNotificationEmitter>>();

                // Resolve ALL registered emitters
                var emitters = builder.Emitters
                    .Select(type => (INotificationEmitter)sp.GetRequiredService(type))
                    .ToList();

                // Create composite that emits to ALL transports in parallel
                return new CompositeNotificationEmitter(emitters, logger);
            });

            // STEP 6: Register CompositeNotificationReceiver as the public INotificationReceiver
            builder.Services.AddSingleton<INotificationReceiver>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<CompositeNotificationReceiver>>();
                var cache = sp.GetRequiredService<IMemoryCache>();
                var options = sp.GetRequiredService<NotificationOptions>();

                // Resolve ALL registered receivers
                var receivers = new List<INotificationReceiver>();
                foreach (var type in builder.Receivers)
                {
                    // Special handling for RedisNotificationReceiver with multiple streams
                    if (type.Name == "RedisNotificationReceiver" &&
                        builder.Properties.ContainsKey("Redis.StreamNames"))
                    {
                        var streamNames = (List<string>)builder.Properties["Redis.StreamNames"];
                        var configuration = sp.GetRequiredService<IConfiguration>();

                        // Get RedisNotificationOptions if configured
                        // Merge options from appsettings.json (IOptions) and fluent API
                        object? redisOptions = null;
                        if (builder.Properties.ContainsKey("kdyf.Notifications.Redis.Options"))
                        {
                            var optionsType = builder.Properties["kdyf.Notifications.Redis.Options"].GetType();

                            // Get options from appsettings.json (IOptions<RedisNotificationOptions>)
                            var iOptionsType = typeof(Microsoft.Extensions.Options.IOptions<>).MakeGenericType(optionsType);
                            var optionsFromConfig = sp.GetService(iOptionsType);
                            object? configValue = null;
                            if (optionsFromConfig != null)
                            {
                                var valueProperty = iOptionsType.GetProperty("Value");
                                configValue = valueProperty?.GetValue(optionsFromConfig);
                            }

                            // Get options from fluent API
                            var optionsFromFluent = sp.GetService(optionsType);

                            // Use fluent if available, otherwise use config
                            redisOptions = optionsFromFluent ?? configValue;
                        }

                        // Resolve default stream name if needed
                        string defaultStreamName = "notifications:stream:default";
                        if (redisOptions != null)
                        {
                            // Use reflection to get DefaultStreamName from RedisNotificationOptions
                            var optionsType = redisOptions.GetType();
                            var storageProperty = optionsType.GetProperty("Storage");
                            if (storageProperty != null)
                            {
                                var storage = storageProperty.GetValue(redisOptions);
                                if (storage != null)
                                {
                                    var defaultStreamNameProperty = storage.GetType().GetProperty("DefaultStreamName");
                                    if (defaultStreamNameProperty != null)
                                    {
                                        var value = defaultStreamNameProperty.GetValue(storage) as string;
                                        if (!string.IsNullOrWhiteSpace(value))
                                        {
                                            defaultStreamName = value;
                                        }
                                    }
                                }
                            }
                        }

                        // Create one receiver instance per stream name
                        foreach (var streamNameOrNull in streamNames)
                        {
                            // Resolve null/empty to default stream name
                            var streamName = string.IsNullOrEmpty(streamNameOrNull) ? defaultStreamName : streamNameOrNull;

                            // Resolve IConnectionMultiplexer (for Redis receivers)
                            var redisConnectionMultiplexerType = typeof(StackExchange.Redis.IConnectionMultiplexer);
                            var redis = sp.GetRequiredService(redisConnectionMultiplexerType);

                            // Resolve ILogger<RedisNotificationReceiver> using the generic GetRequiredService method
                            var loggerType = typeof(ILogger<>).MakeGenericType(type);

                            // Use reflection to call sp.GetRequiredService<ILogger<RedisNotificationReceiver>>()
                            var getServiceMethod = typeof(ServiceProviderServiceExtensions)
                                .GetMethod(nameof(ServiceProviderServiceExtensions.GetRequiredService),
                                           new[] { typeof(IServiceProvider) })
                                ?.MakeGenericMethod(loggerType);

                            var redisLogger = getServiceMethod?.Invoke(null, new object[] { sp });

                            // Resolve NotificationTypeResolver
                            var typeResolver = sp.GetRequiredService<NotificationTypeResolver>();

                            // Resolve RedisStreamParser and RedisStreamInitializer (only for Redis receivers)
                            object? streamParser = null;
                            object? streamInitializer = null;
                            if (type.Name == "RedisNotificationReceiver")
                            {
                                var parserType = type.Assembly.GetType("kdyf.Notifications.Redis.Services.RedisStreamParser");
                                if (parserType != null)
                                {
                                    streamParser = sp.GetRequiredService(parserType);
                                }

                                var initializerType = type.Assembly.GetType("kdyf.Notifications.Redis.Services.RedisStreamInitializer");
                                if (initializerType != null)
                                {
                                    streamInitializer = sp.GetRequiredService(initializerType);
                                }
                            }

                            // Create receiver instance with full stream name using the 8-parameter constructor
                            // Constructor signature: (IConnectionMultiplexer, IConfiguration, ILogger<RedisNotificationReceiver>, NotificationTypeResolver, RedisStreamParser, RedisStreamInitializer, string?, RedisNotificationOptions?)
                            var receiver = (INotificationReceiver)Activator.CreateInstance(
                                type,
                                redis,              // param 1: IConnectionMultiplexer
                                configuration,      // param 2: IConfiguration
                                redisLogger,        // param 3: ILogger<RedisNotificationReceiver>
                                typeResolver,       // param 4: NotificationTypeResolver
                                streamParser,       // param 5: RedisStreamParser
                                streamInitializer,  // param 6: RedisStreamInitializer
                                streamName,         // param 7: string? (full stream name)
                                redisOptions)!;     // param 8: RedisNotificationOptions?

                            receivers.Add(receiver);
                        }
                    }
                    else
                    {
                        // Standard receiver - resolve single instance from DI
                        receivers.Add((INotificationReceiver)sp.GetRequiredService(type));
                    }
                }

                // Create composite with CENTRALIZED DEDUPLICATION
                // This is the ONLY place where deduplication happens
                return new CompositeNotificationReceiver(receivers, cache, options, logger);
            });
        }

    }
}
