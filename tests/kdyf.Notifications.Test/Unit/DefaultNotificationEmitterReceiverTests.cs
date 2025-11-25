using kdyf.Notifications.Interfaces;
using kdyf.Notifications.Services;
using kdyf.Notifications.Integration;
using kdyf.Notifications.Test.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace kdyf.Notifications.Test.Unit
{
    /// <summary>
    /// Integration tests for INotificationEmitter and INotificationReceiver using Composite Pattern.
    /// Tests the coordination between InMemory transport and Composite services.
    /// </summary>
    [TestClass]
    public sealed class DefaultNotificationEmitterReceiverTests
    {
        private ServiceProvider? _serviceProvider;
        private INotificationEmitter? _emitter;
        private INotificationReceiver? _receiver;

        /// <summary>
        /// Initializes the service provider and notification services before each test.
        /// </summary>
        [TestInitialize]
        public void Setup()
        {
            // Build minimal configuration (not needed for InMemory tests, but required by builder)
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection()
                .Build();

            // Configure services
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddKdyfNotification(configuration)
                .Build();

            // Build service provider and get services
            _serviceProvider = services.BuildServiceProvider();
            _emitter = _serviceProvider.GetRequiredService<INotificationEmitter>();
            _receiver = _serviceProvider.GetRequiredService<INotificationReceiver>();
        }

        /// <summary>
        /// Cleans up the service provider after each test.
        /// </summary>
        [TestCleanup]
        public void Cleanup()
        {
            _serviceProvider?.Dispose();
        }

        #region Notify Test
        /// <summary>
        /// Tests that Notify emits a notification successfully.
        /// </summary>
        [TestMethod]
        public async Task Notify_ShouldEmitNotification_WhenEntityIsValid()
        {
            // Arrage
            var entity = new TestNotificationEntity { Message = "Test message" };
            var received = false;
            using var cts = new CancellationTokenSource();

            var subscription = _receiver!.Receive(cts.Token)
                .Subscribe(e =>
                {
                    received = true;
                    Assert.AreEqual("Test message", ((TestNotificationEntity)e).Message);
                });

            // Act
            await _emitter!.NotifyAsync(entity);
            await Task.Delay(100);

            // Assert
            Assert.IsTrue(received);
            subscription.Dispose();
        }

        /// <summary>
        /// Tests that NotifyAsync throws ArgumentNullException when entity is null.
        /// </summary>
        [TestMethod]
        public async Task NotifyAsync_ShouldThrowArgumentNullException_WhenEntityIsNull()
        {
            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentNullException>(async () =>
            {
                await _emitter!.NotifyAsync<TestNotificationEntity>(null);
            });
        }
        #endregion

        #region Receive Tests
        [TestMethod]
        public async Task Receive_ShouldReceiveAllNotifications_WhenNoTagsSpecified()
        {
            // Arrange
            var receivedCount = 0;
            using var cts = new CancellationTokenSource();

            var subscription = _receiver!.Receive(cts.Token)
                .Subscribe(e => receivedCount++);

            // Act
            await _emitter!.NotifyAsync(new TestNotificationEntity { NotificationId = Guid.NewGuid().ToString(), Message = "Message 1" });
            await _emitter!.NotifyAsync(new TestNotificationEntity { NotificationId = Guid.NewGuid().ToString(), Message = "Message 2" });
            await _emitter!.NotifyAsync(new TestNotificationEntity { NotificationId = Guid.NewGuid().ToString(), Message = "Message 3" });

            await Task.Delay(200);

            // Assert
            Assert.AreEqual(3, receivedCount);
            subscription.Dispose();
        }

        /// <summary>
        /// Tests that Receive filters notifications by tags correctly.
        /// </summary>
        [TestMethod]
        public async Task Receive_ShouldFilterByTags_WhenTagsAreSpecified()
        {
            // Arrange
            var receivedMessages = new List<string>();
            using var cts = new CancellationTokenSource();

            var subscription = _receiver!.Receive(cts.Token, "important", "urgent")
                .Subscribe(e => receivedMessages.Add(((TestNotificationEntity)e).Message));

            // Act
            await _emitter!.NotifyAsync(new TestNotificationEntity
            {
                NotificationId = Guid.NewGuid().ToString(),
                Message = "Should receive",
                Tags = new HashSet<string> { "important", "urgent", "extra" }
            });

            await _emitter!.NotifyAsync(new TestNotificationEntity
            {
                NotificationId = Guid.NewGuid().ToString(),
                Message = "Should also receive",
                Tags = new HashSet<string> { "important" }
            });

            await _emitter!.NotifyAsync(new TestNotificationEntity
            {
                NotificationId = Guid.NewGuid().ToString(),
                Message = "Should also receive (urgent only)",
                Tags = new HashSet<string> { "urgent" }
            });

            await _emitter!.NotifyAsync(new TestNotificationEntity
            {
                NotificationId = Guid.NewGuid().ToString(),
                Message = "Should NOT receive",
                Tags = new HashSet<string> { "info" }
            });

            await Task.Delay(200);

            // Assert
            Assert.AreEqual(3, receivedMessages.Count);
            Assert.IsTrue(receivedMessages.Contains("Should receive"));
            Assert.IsTrue(receivedMessages.Contains("Should also receive"));
            Assert.IsTrue(receivedMessages.Contains("Should also receive (urgent only)"));
            Assert.IsFalse(receivedMessages.Contains("Should NOT receive"));
            subscription.Dispose();
        }

        /// <summary>
        /// Tests that Receive generic method filters by type correctly.
        /// </summary>
        [TestMethod]
        public async Task Receive_Generic_ShouldFilterByType_WhenSpecificTypeRequested()
        {
            // Arrange
            var receivedCount = 0;
            using var cts = new CancellationTokenSource();

            var subscription = _receiver!.Receive<TestNotificationEntity>(cts.Token)
                .Subscribe(e => receivedCount++);

            // Act
            await _emitter!.NotifyAsync(new TestNotificationEntity { NotificationId = Guid.NewGuid().ToString(), Message = "Test 1" });
            await _emitter!.NotifyAsync(new AnotherTestNotificationEntity { NotificationId = Guid.NewGuid().ToString(), Data = "Test 2" });
            await _emitter!.NotifyAsync(new TestNotificationEntity { NotificationId = Guid.NewGuid().ToString(), Message = "Test 3" });

            await Task.Delay(200);

            // Assert
            Assert.AreEqual(2, receivedCount);
            subscription.Dispose();
        }

        #endregion

        #region CancellationToken Tests
        /// <summary>
        /// Tests that subscription stops receiving when cancellation token is cancelled.
        /// </summary>
        [TestMethod]
        public async Task Receive_ShouldStopReceiving_WhenCancellationTokenIsCancelled()
        {
            // Arrange
            var receivedCount = 0;
            using var cts = new CancellationTokenSource();

            var subscription = _receiver!.Receive(cts.Token)
                .Subscribe(e => receivedCount++);

            // Act
            await _emitter!.NotifyAsync(new TestNotificationEntity { NotificationId = Guid.NewGuid().ToString(), Message = "Before cancel" });
            await Task.Delay(100);

            cts.Cancel();
            await Task.Delay(100);

            await _emitter!.NotifyAsync(new TestNotificationEntity { NotificationId = Guid.NewGuid().ToString(), Message = "After cancel" });
            await Task.Delay(100);

            // Assert
            Assert.AreEqual(1, receivedCount);
            subscription.Dispose();
        }
        #endregion

        #region Thread Safety Tests
        /// <summary>
        /// Tests that multiple threads can emit notifications concurrently without issues.
        /// </summary>
        [TestMethod]
        public async Task Notify_ShouldBeThreadSafe_WhenCalledFromMultipleThreads()
        {
            // Arrange
            var receivedCount = 0;
            var expectedCount = 100;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var tcs = new TaskCompletionSource();

            var subscription = _receiver!.Receive(cts.Token)
                .Subscribe(e =>
                {
                    Interlocked.Increment(ref receivedCount);
                    if (receivedCount == expectedCount)
                        tcs.TrySetResult();
                });

            // Act
            var tasks = Enumerable.Range(0, expectedCount)
                .Select(i => Task.Run(async () =>
                {
                    await _emitter!.NotifyAsync(new TestNotificationEntity { NotificationId = Guid.NewGuid().ToString(), Message = $"Message {i}" });
                }))
                .ToArray();

            await Task.WhenAll(tasks);
            await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(5), cts.Token));

            // Assert
            Assert.AreEqual(expectedCount, receivedCount, "Not all notifications were received.");
            subscription.Dispose();
        }
        #endregion
    }
}
