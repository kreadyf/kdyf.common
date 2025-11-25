using kdyf.Notifications.Test.Redis.Helpers;
using kdyf.Notifications.Test.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using StackExchange.Redis;
using System.Collections.Concurrent;
using System.Reactive.Linq;

namespace kdyf.Notifications.Test.Redis.Integration
{
    /// <summary>
    /// Example tests demonstrating how to use TestApplicationHost.
    /// Shows different patterns for sending messages and subscribing to notifications.
    /// </summary>
    [TestClass]
    [DoNotParallelize]
    public class ApplicationHostTests
    {
        private IConfiguration? _redisConfiguration;
        private IConnectionMultiplexer? _redis;
        private string _testStreamName = null!;

        /// <summary>
        /// Test context for writing diagnostic output
        /// </summary>
        public TestContext? TestContext { get; set; }

        [TestInitialize]
        public void Setup()
        {
            // Generate unique stream name for this test
            var uniqueId = Guid.NewGuid().ToString("N");
            _testStreamName = $"notifications:stream:test-{uniqueId}";

            // Read Redis configuration from appsettings/user-secrets
            var baseConfiguration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                .AddJsonFile("appsettings.Test.json", optional: true, reloadOnChange: false)
                .AddUserSecrets(System.Reflection.Assembly.GetExecutingAssembly(), optional: true)
                .AddEnvironmentVariables()
                .Build();

            var redisConnectionString = baseConfiguration.GetSection("Redis")["ConnectionString"];

            if (string.IsNullOrWhiteSpace(redisConnectionString))
            {
                throw new InvalidOperationException("Redis:ConnectionString not configured in appsettings.json or user secrets.");
            }

            // Configure Redis for the test
            _redisConfiguration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    ["Redis:ConnectionString"] = redisConnectionString,
                    ["Redis:ConsumerGroup"] = $"test-group-{uniqueId}",
                    ["Redis:MessageTTL"] = "300",
                    ["Redis:MaxConcurrentProcessing"] = "10"
                }!)
                .Build();

            // Create connection for cleanup
            var options = ConfigurationOptions.Parse(redisConnectionString);
            options.AbortOnConnectFail = false;
            _redis = ConnectionMultiplexer.Connect(options);
        }

        [TestCleanup]
        public async Task Cleanup()
        {
            if (_redis != null)
            {
                var db = _redis.GetDatabase();

                // Clean up test streams
                await db.KeyDeleteAsync($"{_testStreamName}:orders");
                await db.KeyDeleteAsync($"{_testStreamName}:payments");
                await db.KeyDeleteAsync(_testStreamName);

                _redis.Dispose();
            }
        }

        /// <summary>
        /// Example test: Verify that sent messages match received messages.
        /// Demonstrates how to validate end-to-end message flow with content verification.
        /// </summary>
        [TestMethod]
        [TestCategory("Integration")]
        public async Task HostExample_VerifyMessagesSentAndReceived()
        {
            // Arrange: Create host that both sends and receives
            var testHost = await TestApplicationHost.CreateAsync(new TestApplicationConfiguration
            {
                ApplicationName = "TestApp",
                RedisConfiguration = _redisConfiguration!,
                ConfigureRedisTarget = cfg => cfg
                    .WithStreamOnly<TestNotificationEntity>("orders"),
                ConfigureRedisSource = cfg => cfg
                    .WithStreams("orders")
            });

            // Setup message collection
            const int expectedMessageCount = 3;
            var (receivedMessages, allMessagesReceived) = CreateMessageCollector(expectedMessageCount);
            var subscription = SubscribeAndCollect(testHost, receivedMessages, allMessagesReceived, expectedMessageCount);

            try
            {
                // Wait for receiver to initialize
                await Task.Delay(3000);

                // Act: Send test messages
                var sentMessages = await SendTestMessages(testHost, expectedMessageCount);

                // Wait for all messages to be received
                var received = allMessagesReceived.Wait(TimeSpan.FromSeconds(30));

                // Assert: Verify all messages were received
                Assert.IsTrue(received, $"Timeout: Expected {expectedMessageCount} messages but only received {receivedMessages.Count}");
                Assert.AreEqual(expectedMessageCount, receivedMessages.Count, "Should receive all sent messages");

                // Assert: Verify message content integrity
                ValidateMessagesReceived(sentMessages, receivedMessages);

                TestContext?.WriteLine($"✓ Test successful: {sentMessages.Count} messages sent and {receivedMessages.Count} received correctly");
            }
            finally
            {
                subscription.Dispose();
                await testHost.DisposeAsync();
            }
        }


        /// <summary>
        /// Test deduplication with multiple sources (Redis + InMemory) and multiple targets (Redis + InMemory).
        /// Verifies that notifications are not duplicated when received from multiple sources.
        /// </summary>
        [TestMethod]
        [TestCategory("Integration")]
        [TestCategory("Deduplication")]
        public async Task MultipleSourcesAndTargets_ShouldDeduplicateNotifications()
        {
            // Arrange: Create host with both Redis and InMemory targets/sources
            var testHost = await TestApplicationHost.CreateAsync(new TestApplicationConfiguration
            {
                ApplicationName = "DualSourceTargetApp",
                RedisConfiguration = _redisConfiguration!,
                ConfigureRedisTarget = cfg => cfg
                    .WithStreamOnly<TestNotificationEntity>("dedup-test"),
                AddInMemoryTarget = true, // Add InMemory target
                ConfigureRedisSource = cfg => cfg
                    .WithStreams("dedup-test"),
                AddInMemorySource = true // Add InMemory source
            });

            // Setup message collection
            const int expectedMessageCount = 3;
            var (receivedNotifications, allMessagesReceived) = CreateMessageCollector(expectedMessageCount);
            var subscription = SubscribeAndCollect(testHost, receivedNotifications, allMessagesReceived, expectedMessageCount);

            try
            {
                // Wait for receivers to initialize
                await Task.Delay(3000);

                // Act: Send messages - they will be sent to BOTH Redis and InMemory targets
                var sentMessages = await SendTestMessages(testHost, expectedMessageCount, "dedup", "Deduplication test message");

                // Wait for all messages to be received or timeout
                var received = allMessagesReceived.Wait(TimeSpan.FromSeconds(30));

                // Assert: Verify deduplication - should receive exactly expectedMessageCount, not double
                Assert.IsTrue(received, $"Timeout: Expected {expectedMessageCount} messages but only received {receivedNotifications.Count}");

                // Key assertion: Should receive EXACTLY expectedMessageCount, NOT double (expectedMessageCount * 2)
                // This proves deduplication is working across multiple sources
                Assert.AreEqual(expectedMessageCount, receivedNotifications.Count,
                    $"Deduplication failed: Expected {expectedMessageCount} unique messages but received {receivedNotifications.Count}. " +
                    $"Messages may have been duplicated from Redis and InMemory sources.");

                // Group by NotificationId to verify no duplicates
                var groupedById = receivedNotifications.GroupBy(n => n.NotificationId).ToList();
                foreach (var group in groupedById)
                {
                    Assert.AreEqual(1, group.Count(),
                        $"Message {group.Key} was received {group.Count()} times - deduplication failed!");
                }

                // Verify all sent messages were received exactly once
                foreach (var sentMessage in sentMessages)
                {
                    var matchingMessages = receivedNotifications.Where(r => r.NotificationId == sentMessage.NotificationId).ToList();
                    Assert.AreEqual(1, matchingMessages.Count,
                        $"Message {sentMessage.NotificationId} should be received exactly once, but was received {matchingMessages.Count} times");

                    var receivedMessage = matchingMessages.First();
                    Assert.AreEqual(sentMessage.Message, receivedMessage.Message,
                        $"Message content mismatch for {sentMessage.NotificationId}");
                }

                TestContext?.WriteLine($"✓ Deduplication test successful: {sentMessages.Count} messages sent to 2 targets, " +
                    $"{receivedNotifications.Count} unique messages received from 2 sources (no duplicates)");
            }
            finally
            {
                subscription.Dispose();
                await testHost.DisposeAsync();
            }
        }

        /// <summary>
        /// Test cross-application communication with separate sender and receiver hosts.
        /// Simulates a microservices architecture where one app sends messages and another receives them via Redis.
        /// Validates that messages sent from SenderApp are correctly received by ReceiverApp.
        /// </summary>
        [TestMethod]
        [TestCategory("Integration")]
        [TestCategory("CrossApplication")]
        public async Task MultipleHost_VerifyMessagesSendAndReceived()
        {
            // Arrange: Create separate sender and receiver applications (simulates microservices)

            // SenderApp: Only has Redis target configured (emits messages)
            // Uses full stream name to ensure sender and receiver use the same stream
            var streamName = $"notifications:stream:default";
            var senderHost = await TestApplicationHost.CreateAsync(new TestApplicationConfiguration
            {
                ApplicationName = "SenderApp",
                RedisConfiguration = _redisConfiguration!,
                ConfigureRedisTarget = cfg => cfg
                    .WithStreamOnly<TestNotificationEntity>(streamName)
            });

            // ReceiverApp: Only has Redis source configured (receives messages)
            // Must use the same full stream name as the sender
            var receiverHost = await TestApplicationHost.CreateAsync(new TestApplicationConfiguration
            {
                ApplicationName = "ReceiverApp",
                RedisConfiguration = _redisConfiguration!,
                ConfigureRedisSource = cfg => cfg
                    .WithStreams(streamName)
            });

            // Setup message collection
            const int expectedMessageCount = 3;
            var (receivedMessages, allMessagesReceived) = CreateMessageCollector(expectedMessageCount);
            var subscription = SubscribeAndCollect(receiverHost, receivedMessages, allMessagesReceived, expectedMessageCount);

            try
            {
                // Wait for receiver to fully initialize (Redis consumer group creation, stream setup)
                // This delay ensures the receiver is ready before sender starts emitting
                await Task.Delay(10000);

                // Act: Send messages from SenderApp
                var sentMessages = await SendTestMessages(senderHost, expectedMessageCount);

                // Wait for all messages to be received
                var received = allMessagesReceived.Wait(TimeSpan.FromSeconds(30));

                // Assert: Verify cross-application communication succeeded
                Assert.IsTrue(received, $"Timeout: Expected {expectedMessageCount} messages but only received {receivedMessages.Count}");
                Assert.AreEqual(expectedMessageCount, receivedMessages.Count, "ReceiverApp should receive all messages sent by SenderApp");

                // Assert: Verify message content integrity across applications
                ValidateMessagesReceived(sentMessages, receivedMessages);

                TestContext?.WriteLine($"✓ Test successful: {sentMessages.Count} messages sent and {receivedMessages.Count} received correctly");
            }
            finally
            {
                subscription.Dispose();
                await senderHost.DisposeAsync();
                await receiverHost.DisposeAsync();
            }
        }

        #region Helper Methods

        /// <summary>
        /// Creates a message collector that signals when the expected count is reached.
        /// </summary>
        /// <param name="expectedCount">Number of messages expected before signaling.</param>
        /// <returns>Tuple containing the message collection and signal.</returns>
        private (ConcurrentBag<TestNotificationEntity> messages, ManualResetEventSlim signal)
            CreateMessageCollector(int expectedCount)
        {
            var messages = new ConcurrentBag<TestNotificationEntity>();
            var signal = new ManualResetEventSlim(false);
            return (messages, signal);
        }

        /// <summary>
        /// Creates a subscription that collects messages and signals when count is reached.
        /// </summary>
        /// <param name="host">Application host to subscribe to.</param>
        /// <param name="collection">Collection to store received messages.</param>
        /// <param name="signal">Signal to set when expected count is reached.</param>
        /// <param name="expectedCount">Number of messages expected.</param>
        /// <returns>Disposable subscription.</returns>
        private IDisposable SubscribeAndCollect(
            TestApplicationHost host,
            ConcurrentBag<TestNotificationEntity> collection,
            ManualResetEventSlim signal,
            int expectedCount)
        {
            return host.Subscribe<TestNotificationEntity>(notification =>
            {
                TestContext?.WriteLine($"Received: {notification.NotificationId} - {notification.Message}");
                collection.Add(notification);

                if (collection.Count >= expectedCount)
                {
                    signal.Set();
                }
            });
        }

        /// <summary>
        /// Sends a batch of test messages with the specified prefix.
        /// </summary>
        /// <param name="host">Application host to send messages from.</param>
        /// <param name="count">Number of messages to send.</param>
        /// <param name="idPrefix">Prefix for message IDs (default: "order").</param>
        /// <param name="messagePrefix">Prefix for message text (default: "Order").</param>
        /// <returns>List of sent messages.</returns>
        private async Task<List<TestNotificationEntity>> SendTestMessages(
            TestApplicationHost host,
            int count,
            string idPrefix = "order",
            string messagePrefix = "Order")
        {
            var sentMessages = new List<TestNotificationEntity>();
            for (int i = 1; i <= count; i++)
            {
                var message = new TestNotificationEntity
                {
                    NotificationId = $"{idPrefix}-{i}",
                    Message = $"{messagePrefix} #{i}",
                    Timestamp = DateTime.UtcNow,
                    Tags = new HashSet<string> { "test", idPrefix }
                };

                sentMessages.Add(message);
                await host.EmitAsync(message);
                TestContext?.WriteLine($"Sent: {message.NotificationId} - {message.Message}");
            }
            return sentMessages;
        }

        /// <summary>
        /// Validates that all sent messages were received with correct content.
        /// </summary>
        /// <param name="sentMessages">Messages that were sent.</param>
        /// <param name="receivedMessages">Messages that were received.</param>
        private void ValidateMessagesReceived(
            List<TestNotificationEntity> sentMessages,
            ConcurrentBag<TestNotificationEntity> receivedMessages)
        {
            foreach (var sentMessage in sentMessages)
            {
                var matchingReceived = receivedMessages.FirstOrDefault(r => r.NotificationId == sentMessage.NotificationId);
                Assert.IsNotNull(matchingReceived, $"Message with ID {sentMessage.NotificationId} was not received");
                Assert.AreEqual(sentMessage.Message, matchingReceived.Message,
                    $"Message content mismatch for {sentMessage.NotificationId}");
                Assert.IsTrue(matchingReceived.Tags.Contains("test"),
                    $"Message {sentMessage.NotificationId} should contain 'test' tag");
            }
        }

        #endregion
    }
}

