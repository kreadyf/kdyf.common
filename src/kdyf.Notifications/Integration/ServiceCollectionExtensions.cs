using kdyf.Notifications.Configuration;
using kdyf.Notifications.Interfaces;
using kdyf.Notifications.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;

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
        /// Supports multiple receiver factories (Redis, Kafka, RabbitMQ, etc.) registered simultaneously.
        /// Each factory checks its own properties and creates receivers only for its transport.
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

            // VALIDATION: Ensure at least one receiver source is configured
            // Receivers can come from builder.Receivers (simple) or factories (complex like Redis, Kafka)
            var hasFactoryConfiguration = builder.Services.Any(sd =>
                sd.ServiceType == typeof(INotificationReceiverFactory));

            if (builder.Receivers.Count == 0 && !hasFactoryConfiguration)
            {
                throw new InvalidOperationException(
                    "No notification receivers registered. At least one receiver (e.g., InMemory, Redis) must be configured.");
            }

            // STEP 1: Validate and register NotificationOptions
            builder.Options.Validate();
            builder.Services.AddSingleton(builder.Options);

            // STEP 2: Register MemoryCache with configured size limits
            builder.Services.TryAddSingleton<IMemoryCache>(sp =>
            {
                var options = sp.GetRequiredService<NotificationOptions>();
                return new MemoryCache(options.ToMemoryCacheOptions());
            });

            // STEP 3: Register CompositeNotificationEmitter as the public INotificationEmitter
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

            // STEP 4: Register CompositeNotificationReceiver as the public INotificationReceiver
            // Capture properties for use in the factory lambda
            var builderProperties = builder.Properties;

            builder.Services.AddSingleton<INotificationReceiver>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<CompositeNotificationReceiver>>();
                var cache = sp.GetRequiredService<IMemoryCache>();
                var options = sp.GetRequiredService<NotificationOptions>();

                var receivers = new List<INotificationReceiver>();

                // 1. Resolve simple receivers from DI (InMemory, etc.)
                foreach (var type in builder.Receivers)
                {
                    receivers.Add((INotificationReceiver)sp.GetRequiredService(type));
                }

                // 2. Use ALL registered factories for receivers that need special creation logic
                // Each factory (Redis, Kafka, RabbitMQ, etc.) checks its own properties
                // and returns receivers only for its transport
                var receiverFactories = sp.GetServices<INotificationReceiverFactory>();
                foreach (var factory in receiverFactories)
                {
                    var factoryReceivers = factory.CreateReceivers(sp, builderProperties);
                    receivers.AddRange(factoryReceivers);
                }

                // Create composite with CENTRALIZED DEDUPLICATION
                // This is the ONLY place where deduplication happens
                return new CompositeNotificationReceiver(receivers, cache, options, logger);
            });
        }

    }
}
