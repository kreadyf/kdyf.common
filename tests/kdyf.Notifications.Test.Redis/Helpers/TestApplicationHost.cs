using kdyf.Notifications.Integration;
using kdyf.Notifications.Interfaces;
using kdyf.Notifications.Redis.Configuration;
using kdyf.Notifications.Redis.Integration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace kdyf.Notifications.Test.Redis.Helpers
{
    /// <summary>
    /// Fluent configuration for TestApplicationHost.
    /// Allows configuring sources, targets and Redis options flexibly from unit tests.
    /// </summary>
    public class TestApplicationConfiguration
    {
        /// <summary>
        /// Application name (for log identification)
        /// </summary>
        public string ApplicationName { get; set; } = "TestApp";

        /// <summary>
        /// Redis configuration (ConnectionString, StreamName, ConsumerGroup, etc.)
        /// </summary>
        public IConfiguration RedisConfiguration { get; set; } = null!;

        /// <summary>
        /// Redis Target configuration (emission).
        /// Example: configure => configure.WithStreamOnly<MyNotification>("my-stream")
        /// </summary>
        public Action<RedisEmitterConfiguration>? ConfigureRedisTarget { get; set; }

        /// <summary>
        /// Redis Source configuration (reception).
        /// Example: configure => configure.WithStreams("stream1", "stream2")
        /// </summary>
        public Action<RedisReceiverConfiguration>? ConfigureRedisSource { get; set; }

        /// <summary>
        /// Redis options configuration (performance, resilience, etc.)
        /// Example: opts => opts.Performance.ChannelCapacity = 20000
        /// </summary>
        public Action<RedisNotificationOptions>? ConfigureRedisOptions { get; set; }

        /// <summary>
        /// Add InMemory target (for hybrid tests)
        /// </summary>
        public bool AddInMemoryTarget { get; set; }

        /// <summary>
        /// Add InMemory source (for hybrid tests)
        /// </summary>
        public bool AddInMemorySource { get; set; }

        /// <summary>
        /// Minimum logging level
        /// </summary>
        public LogLevel MinimumLogLevel { get; set; } = LogLevel.Warning;
    }

    /// <summary>
    /// Testing wrapper that encapsulates a complete application with its own host.
    /// Simulates a real application (like Sample01 or Sample02) for integration tests.
    /// Each instance represents an independent application that communicates via Redis.
    /// </summary>
    public class TestApplicationHost : IAsyncDisposable
    {
        private readonly IHost _host;
        private readonly INotificationEmitter _emitter;
        private readonly INotificationReceiver _receiver;

        /// <summary>
        /// Application name
        /// </summary>
        public string ApplicationName { get; }

        private TestApplicationHost(string appName, IHost host)
        {
            ApplicationName = appName;
            _host = host;
            _emitter = host.Services.GetRequiredService<INotificationEmitter>();
            _receiver = host.Services.GetRequiredService<INotificationReceiver>();
        }

        /// <summary>
        /// Creates a test application with complete and fluent configuration.
        /// </summary>
        /// <param name="configuration">Application configuration (targets, sources, options)</param>
        /// <returns>TestApplicationHost instance ready to use</returns>
        public static async Task<TestApplicationHost> CreateAsync(
            TestApplicationConfiguration configuration)
        {
            var host = Host.CreateDefaultBuilder()
                .ConfigureAppConfiguration((context, config) =>
                {
                    config.AddConfiguration(configuration.RedisConfiguration);
                })
                .ConfigureServices((context, services) =>
                {
                    // Logging
                    services.AddLogging(builder => builder
                        .AddConsole()
                        .SetMinimumLevel(configuration.MinimumLogLevel));

                    // Memory cache (required by Redis)
                    services.AddMemoryCache();

                    // Configure notification builder
                    var builder = services.AddKdyfNotification(context.Configuration);

                    // Configure targets
                    if (configuration.ConfigureRedisTarget != null)
                    {
                        builder.AddRedisTarget(configuration.ConfigureRedisTarget);
                    }

                    if (configuration.AddInMemoryTarget)
                    {
                        builder.AddInMemoryTarget();
                    }

                    // Configure sources
                    if (configuration.ConfigureRedisSource != null)
                    {
                        // If Redis source is configured but no Redis target was configured,
                        // we still need to register IConnectionMultiplexer
                        // AddRedisTarget normally registers it, but for receiver-only apps we need to do it here
                        if (configuration.ConfigureRedisTarget == null)
                        {
                            builder.AddRedisTarget(); // This registers IConnectionMultiplexer and other Redis infrastructure
                        }

                        builder.AddRedisSource(configuration.ConfigureRedisSource);
                    }

                    if (configuration.AddInMemorySource)
                    {
                        builder.AddInMemorySource();
                    }

                    // Configure Redis options
                    if (configuration.ConfigureRedisOptions != null)
                    {
                        builder.ConfigureRedisOptions(configuration.ConfigureRedisOptions);
                    }

                    builder.Build();
                })
                .Build();

            await host.StartAsync();

            return new TestApplicationHost(configuration.ApplicationName, host);
        }

        /// <summary>
        /// Sends a notification (simulates app emission).
        /// </summary>
        /// <typeparam name="TNotification">Notification type</typeparam>
        /// <param name="notification">Notification to send</param>
        /// <returns>Task that completes when the notification is sent</returns>
        public Task EmitAsync<TNotification>(TNotification notification)
            where TNotification : class, INotificationEntity
        {
            return _emitter.NotifyAsync(notification);
        }

        /// <summary>
        /// Subscribes to notifications and executes callback when they arrive.
        /// </summary>
        /// <typeparam name="TNotification">Notification type to receive</typeparam>
        /// <param name="onReceived">Callback to execute for each received message</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <param name="tags">Optional tags to filter messages</param>
        /// <returns>IDisposable to cancel the subscription</returns>
        public IDisposable Subscribe<TNotification>(
            Action<TNotification> onReceived,
            CancellationToken cancellationToken = default,
            params string[] tags)
            where TNotification : class, INotificationEntity
        {
            return _receiver
                .Receive<TNotification>(cancellationToken, tags)
                .Subscribe(onReceived);
        }

        /// <summary>
        /// Subscribes and collects messages in a thread-safe ConcurrentBag.
        /// Useful for tests that need to verify all received messages.
        /// </summary>
        /// <typeparam name="TNotification">Notification type to receive</typeparam>
        /// <param name="collection">ConcurrentBag where messages are stored</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <param name="tags">Optional tags to filter messages</param>
        /// <returns>IDisposable to cancel the subscription</returns>
        public IDisposable CollectMessages<TNotification>(
            ConcurrentBag<TNotification> collection,
            CancellationToken cancellationToken = default,
            params string[] tags)
            where TNotification : class, INotificationEntity
        {
            return _receiver
                .Receive<TNotification>(cancellationToken, tags)
                .Subscribe(notification => collection.Add(notification));
        }

        /// <summary>
        /// Direct access to the observable (for advanced tests that need Rx operators).
        /// Allows using operators like Where, Select, Take, Buffer, etc.
        /// </summary>
        /// <typeparam name="TNotification">Notification type to observe</typeparam>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <param name="tags">Optional tags to filter messages</param>
        /// <returns>Observable of notifications</returns>
        public IObservable<TNotification> Observe<TNotification>(
            CancellationToken cancellationToken = default,
            params string[] tags)
            where TNotification : class, INotificationEntity
        {
            return _receiver.Receive<TNotification>(cancellationToken, tags);
        }

        /// <summary>
        /// Stops and releases host resources.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            await _host.StopAsync();
            _host.Dispose();
        }
    }
}
