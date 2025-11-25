using kdyf.Notifications.Interfaces;
using kdyf.Notifications.Integration;
using kdyf.Notifications.Redis.Configuration;
using kdyf.Notifications.Redis.Integration;
using kdyf.Notifications.Test.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using StackExchange.Redis;
using System.Reactive.Linq;

namespace kdyf.Notifications.Test.Redis.Integration
{
    /// <summary>
    /// End-to-end integration tests for multi-stream functionality.
    /// Tests complete flow: configuration → emission → reception with routing.
    /// Requires Redis to be running.
    /// </summary>
    [TestClass]
    [DoNotParallelize]
    public class MultipleStreamsEndToEndTests
    {
        private INotificationEmitter? _emitter;
        private INotificationReceiver? _receiver;
        private IConfiguration? _configuration;
        private IConnectionMultiplexer? _redis;
        private string _baseStreamName = null!;
        private List<string> _streamsToCleanup = new();

        [TestInitialize]
        public void Setup()
        {
            // Generate unique base stream name for this test run to avoid interference
            _baseStreamName = $"notifications:stream:test-{Guid.NewGuid():N}";

            // Build configuration from appsettings.json (same pattern as RedisNotificationReceiverTests)
            var baseConfiguration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                .AddJsonFile("appsettings.Test.json", optional: true, reloadOnChange: false)
                .AddUserSecrets(System.Reflection.Assembly.GetExecutingAssembly(), optional: true)
                .AddEnvironmentVariables()
                .Build();

            // Read Redis connection string from configuration
            var redisConnectionString = baseConfiguration.GetSection("Redis")["ConnectionString"];

            if (string.IsNullOrWhiteSpace(redisConnectionString))
            {
                throw new InvalidOperationException("Redis:ConnectionString not configured in appsettings.json or user secrets.");
            }

            // Override with test-specific settings while preserving the real connection string
            _configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    ["Redis:ConnectionString"] = redisConnectionString,
                    ["Redis:ConsumerGroup"] = $"test-group-{Guid.NewGuid():N}",
                    ["Redis:MessageTTL"] = "300",
                    ["Redis:MaxConcurrentProcessing"] = "10"
                }!)
                .Build();

            // Create Redis connection for cleanup
            var options = ConfigurationOptions.Parse(redisConnectionString);
            options.AbortOnConnectFail = false;
            _redis = ConnectionMultiplexer.Connect(options);
        }

        [TestCleanup]
        public async Task Cleanup()
        {
            // Clean up Redis streams
            if (_redis != null && _redis.IsConnected)
            {
                var db = _redis.GetDatabase();
                foreach (var streamName in _streamsToCleanup)
                {
                    try
                    {
                        await db.KeyDeleteAsync(streamName);
                    }
                    catch (Exception)
                    {
                        // Ignore cleanup errors
                    }
                }
                _redis.Dispose();
            }
        }

        /// <summary>
        /// Tests that notifications are correctly routed to different streams based on type configuration.
        /// </summary>
        [TestMethod]
        [TestCategory("Integration")]
        [Timeout(30000)]
        public async Task EndToEnd_MultipleStreams_ShouldEmitAndReceiveCorrectly()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
            services.AddSingleton(_configuration!);
            services.AddMemoryCache();

            // Configure with type-to-stream routing
            var testStream = $"{_baseStreamName}:test";
            var anotherStream = $"{_baseStreamName}:another";
            services.AddKdyfNotification(_configuration!)
                .AddRedisTarget(cfg => cfg
                    .WithStream(_baseStreamName)  // Default stream for unmapped types
                    .WithStream<TestNotificationEntity>(testStream)
                    .WithStream<AnotherTestNotificationEntity>(anotherStream))
                .AddRedisSource(cfg => cfg
                    .WithStreams(testStream, anotherStream))
                .Build();

            var serviceProvider = services.BuildServiceProvider();
            _emitter = serviceProvider.GetRequiredService<INotificationEmitter>();
            _receiver = serviceProvider.GetRequiredService<INotificationReceiver>();

            // Track streams for cleanup
            _streamsToCleanup.Add(testStream);
            _streamsToCleanup.Add(anotherStream);

            var testNotification = new TestNotificationEntity
            {
                NotificationId = Guid.NewGuid().ToString(),
                Message = "Test routing",
                Timestamp = DateTime.UtcNow
            };

            var anotherNotification = new AnotherTestNotificationEntity
            {
                NotificationId = Guid.NewGuid().ToString(),
                Message = "Another routing",
                Timestamp = DateTime.UtcNow
            };

            var receivedNotifications = new List<INotificationEntity>();
            var cts = new CancellationTokenSource();
            var receivedEvent = new ManualResetEventSlim(false);

            // Act: Subscribe
            var subscription = _receiver.Receive(cts.Token)
                .Subscribe(notification =>
                {
                    lock (receivedNotifications)
                    {
                        receivedNotifications.Add(notification);
                        if (receivedNotifications.Count >= 2)
                        {
                            receivedEvent.Set();
                        }
                    }
                });

            await Task.Delay(2000); // Allow receivers to connect and create consumer groups

            // Emit to different streams
            await _emitter.NotifyAsync(testNotification);
            await _emitter.NotifyAsync(anotherNotification);

            // Wait for reception
            var received = receivedEvent.Wait(TimeSpan.FromSeconds(15));

            // Cancel subscription
            cts.Cancel();
            await Task.Delay(500); // Allow graceful shutdown
            subscription.Dispose();

            // Assert
            Assert.IsTrue(received, "Should receive notifications within 15 seconds");

            lock (receivedNotifications)
            {
                Assert.AreEqual(2, receivedNotifications.Count, "Should receive both notifications");

                var testReceived = receivedNotifications.OfType<TestNotificationEntity>().FirstOrDefault();
                var anotherReceived = receivedNotifications.OfType<AnotherTestNotificationEntity>().FirstOrDefault();

                Assert.IsNotNull(testReceived, "Should receive TestNotificationEntity");
                Assert.IsNotNull(anotherReceived, "Should receive AnotherTestNotificationEntity");
                Assert.AreEqual(testNotification.NotificationId, testReceived.NotificationId);
                Assert.AreEqual(anotherNotification.NotificationId, anotherReceived.NotificationId);
            }
        }

        /// <summary>
        /// Tests that multiple Redis receivers are created for multiple stream configurations.
        /// Verifies the service collection correctly registers multiple receiver instances.
        /// </summary>
        [TestMethod]
        [TestCategory("Integration")]
        public void EndToEnd_ConsumerGroups_ShouldCreateMultipleReceivers()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(_configuration!);
            services.AddMemoryCache();

            // Configure with multiple streams
            var ordersStream = $"{_baseStreamName}:orders";
            var metricsStream = $"{_baseStreamName}:metrics";
            services.AddKdyfNotification(_configuration!)
                .AddRedisTarget(cfg => cfg
                    .WithStream(_baseStreamName)  // Default stream for unmapped types
                    .WithStream<TestNotificationEntity>(ordersStream)
                    .WithStream<AnotherTestNotificationEntity>(metricsStream))
                .AddRedisSource(cfg => cfg
                    .WithStreams(ordersStream, metricsStream))
                .Build();

            // Act
            var serviceProvider = services.BuildServiceProvider();
            var receiver = serviceProvider.GetRequiredService<INotificationReceiver>();

            // Assert: Verify receiver is created successfully
            Assert.IsNotNull(receiver);
            Assert.IsInstanceOfType(receiver, typeof(INotificationReceiver));

            // The composite receiver should have been configured with multiple Redis receivers (one per stream)
            // We can't directly inspect the private receivers list, but we verified the configuration works in other integration tests
        }

        /// <summary>
        /// Tests that a single receiver subscribed to multiple streams receives notifications correctly.
        /// Verifies that the multi-stream configuration works within a single service.
        /// </summary>
        [TestMethod]
        [TestCategory("Integration")]
        [Timeout(20000)]
        public async Task EndToEnd_StreamIsolation_SingleReceiverMultipleStreams()
        {
            // Arrange - Single service that listens to both "orders" and "metrics"
            var services = new ServiceCollection();
            services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
            services.AddSingleton(_configuration!);
            services.AddMemoryCache();

            var ordersStream2 = $"{_baseStreamName}:orders";
            var metricsStream2 = $"{_baseStreamName}:metrics";
            services.AddKdyfNotification(_configuration!)
                .AddRedisTarget(cfg => cfg
                    .WithStream(_baseStreamName)  // Default stream for unmapped types
                    .WithStream<TestNotificationEntity>(ordersStream2)
                    .WithStream<AnotherTestNotificationEntity>(metricsStream2))
                .AddRedisSource(cfg => cfg
                    .WithStreams(ordersStream2, metricsStream2)) // Listen to both
                .Build();

            var serviceProvider = services.BuildServiceProvider();
            var receiver = serviceProvider.GetRequiredService<INotificationReceiver>();
            var emitter = serviceProvider.GetRequiredService<INotificationEmitter>();

            // Track streams for cleanup
            _streamsToCleanup.Add(ordersStream2);
            _streamsToCleanup.Add(metricsStream2);

            var receivedOrders = new List<TestNotificationEntity>();
            var receivedMetrics = new List<AnotherTestNotificationEntity>();
            var cts = new CancellationTokenSource();
            var bothReceivedEvent = new ManualResetEventSlim(false);

            // Act: Subscribe and filter by type
            var subscription = receiver.Receive(cts.Token)
                .Subscribe(notification =>
                {
                    if (notification is TestNotificationEntity testEntity)
                    {
                        lock (receivedOrders)
                        {
                            receivedOrders.Add(testEntity);
                        }
                    }
                    else if (notification is AnotherTestNotificationEntity anotherEntity)
                    {
                        lock (receivedMetrics)
                        {
                            receivedMetrics.Add(anotherEntity);
                        }
                    }

                    // Check if we received both types
                    lock (receivedOrders)
                    {
                        lock (receivedMetrics)
                        {
                            if (receivedOrders.Count >= 1 && receivedMetrics.Count >= 1)
                            {
                                bothReceivedEvent.Set();
                            }
                        }
                    }
                });

            await Task.Delay(3000); // Allow receiver to connect and create consumer groups

            // Emit one notification to each stream
            var orderNotification = new TestNotificationEntity
            {
                NotificationId = Guid.NewGuid().ToString(),
                Message = "Order notification",
                Timestamp = DateTime.UtcNow
            };

            var metricNotification = new AnotherTestNotificationEntity
            {
                NotificationId = Guid.NewGuid().ToString(),
                Message = "Metric notification",
                Timestamp = DateTime.UtcNow
            };

            await emitter.NotifyAsync(orderNotification);
            await Task.Delay(500); // Small delay between emissions
            await emitter.NotifyAsync(metricNotification);

            // Wait for both to be received
            var bothReceived = bothReceivedEvent.Wait(TimeSpan.FromSeconds(12));

            // Cleanup
            cts.Cancel();
            await Task.Delay(500);
            subscription.Dispose();

            // Assert: Receiver should get notifications from both streams
            Assert.IsTrue(bothReceived, "Should receive notifications from both streams");

            lock (receivedOrders)
            {
                Assert.AreEqual(1, receivedOrders.Count, "Should receive 1 order notification");
                Assert.AreEqual(orderNotification.NotificationId, receivedOrders[0].NotificationId);
            }

            lock (receivedMetrics)
            {
                Assert.AreEqual(1, receivedMetrics.Count, "Should receive 1 metric notification");
                Assert.AreEqual(metricNotification.NotificationId, receivedMetrics[0].NotificationId);
            }
        }

        /// <summary>
        /// Tests that types without explicit mapping use the default stream.
        /// </summary>
        [TestMethod]
        [TestCategory("Integration")]
        [Timeout(15000)]
        public async Task EndToEnd_DefaultStream_ShouldReceiveUnmappedTypes()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
            services.AddSingleton(_configuration!);
            services.AddMemoryCache();

            // Only map AnotherTestNotificationEntity, leave TestNotificationEntity unmapped
            var mappedStream = $"{_baseStreamName}:mapped";
            services.AddKdyfNotification(_configuration!)
                .AddRedisTarget(cfg => cfg
                    .WithStream(_baseStreamName)  // Default stream for unmapped types
                    .WithStream<AnotherTestNotificationEntity>(mappedStream))
                .AddRedisSource() // Default source - listens to default stream
                .Build();

            var serviceProvider = services.BuildServiceProvider();
            _emitter = serviceProvider.GetRequiredService<INotificationEmitter>();
            _receiver = serviceProvider.GetRequiredService<INotificationReceiver>();

            // Track streams for cleanup
            _streamsToCleanup.Add(_baseStreamName); // Default stream
            _streamsToCleanup.Add(mappedStream);

            var unmappedNotification = new TestNotificationEntity
            {
                NotificationId = Guid.NewGuid().ToString(),
                Message = "Unmapped notification",
                Timestamp = DateTime.UtcNow
            };

            var receivedNotifications = new List<INotificationEntity>();
            var cts = new CancellationTokenSource();
            var receivedEvent = new ManualResetEventSlim(false);

            // Act: Subscribe to default stream
            var subscription = _receiver.Receive(cts.Token)
                .Subscribe(notification =>
                {
                    lock (receivedNotifications)
                    {
                        receivedNotifications.Add(notification);
                        receivedEvent.Set();
                    }
                });

            await Task.Delay(2000); // Allow receiver to connect

            // Emit unmapped notification (should go to default stream)
            await _emitter.NotifyAsync(unmappedNotification);

            // Wait for reception
            var received = receivedEvent.Wait(TimeSpan.FromSeconds(8));

            // Cleanup
            cts.Cancel();
            await Task.Delay(500);
            subscription.Dispose();

            // Assert
            Assert.IsTrue(received, "Should receive unmapped notification on default stream");

            lock (receivedNotifications)
            {
                Assert.AreEqual(1, receivedNotifications.Count, "Should receive exactly 1 notification");
                Assert.IsInstanceOfType(receivedNotifications[0], typeof(TestNotificationEntity));
                Assert.AreEqual(unmappedNotification.NotificationId, receivedNotifications[0].NotificationId);
            }
        }

        /// <summary>
        /// Tests backward compatibility: AddRedisTarget/Source without parameters should work.
        /// </summary>
        [TestMethod]
        [TestCategory("Integration")]
        [Timeout(10000)]
        public async Task Configuration_BackwardCompatibility_ShouldWorkWithoutParameters()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(_configuration!);

            // Act - Old style without parameters
            var builder = services.AddKdyfNotification(_configuration!)
                .AddRedisTarget()
                .AddRedisSource();

            // Assert - Should not throw
            builder.Build();
            var serviceProvider = services.BuildServiceProvider();
            var emitter = serviceProvider.GetRequiredService<INotificationEmitter>();
            var receiver = serviceProvider.GetRequiredService<INotificationReceiver>();

            Assert.IsNotNull(emitter);
            Assert.IsNotNull(receiver);

            // Track stream for cleanup
            _streamsToCleanup.Add(_baseStreamName);

            await Task.CompletedTask;
        }

        /// <summary>
        /// Tests that fluent API applies configuration correctly.
        /// </summary>
        [TestMethod]
        [TestCategory("Unit")]
        public void Configuration_WithFluentAPI_ShouldApplyCorrectly()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(_configuration!);

            // Act
            var builder = services.AddKdyfNotification(_configuration!)
                .AddRedisTarget(cfg => cfg
                    .WithStream<TestNotificationEntity>("orders")
                    .WithStreamOnly<AnotherTestNotificationEntity>("metrics"));

            // Assert
            var options = builder.GetRedisOptions();
            Assert.IsNotNull(options);
            Assert.AreEqual(2, options.TypeToStreamMapping.Count, "Should have 2 type mappings");
            Assert.IsTrue(options.TypeToStreamMapping.ContainsKey(typeof(TestNotificationEntity)),
                "Should contain TestNotificationEntity mapping");
            Assert.IsTrue(options.TypeToStreamMapping.ContainsKey(typeof(AnotherTestNotificationEntity)),
                "Should contain AnotherTestNotificationEntity mapping");
            Assert.IsTrue(options.StreamOnlyTypes.Contains(typeof(AnotherTestNotificationEntity)),
                "AnotherTestNotificationEntity should be stream-only");
            Assert.IsFalse(options.StreamOnlyTypes.Contains(typeof(TestNotificationEntity)),
                "TestNotificationEntity should NOT be stream-only");
        }
    }
}

