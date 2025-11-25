using kdyf.Notifications.Interfaces;
using kdyf.Notifications.Services;
using kdyf.Notifications.Test.Models;
using Microsoft.Extensions.Logging;
using Moq;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace kdyf.Notifications.Test.Unit
{
    /// <summary>
    /// Unit tests for the <see cref="CompositeNotificationReceiver"/> class.
    /// Uses mocks - no external dependencies required.
    /// Focuses heavily on testing centralized deduplication - the most critical feature.
    /// </summary>
    [TestClass]
    [TestCategory("UnitTest")]
    public sealed class CompositeNotificationReceiverTests
    {
        private Mock<ILogger<CompositeNotificationReceiver>>? _mockLogger;

        [TestInitialize]
        public void Setup()
        {
            _mockLogger = new Mock<ILogger<CompositeNotificationReceiver>>();
        }

        #region Constructor Tests

        /// <summary>
        /// Tests that the constructor initializes with multiple receivers.
        /// </summary>
        [TestMethod]
        public void Constructor_ShouldAcceptMultipleReceivers()
        {
            // Arrange
            var receiver1 = new Mock<INotificationReceiver>();
            var receiver2 = new Mock<INotificationReceiver>();
            var receivers = new List<INotificationReceiver> { receiver1.Object, receiver2.Object };

            // Act
            var composite = new CompositeNotificationReceiver(receivers, _mockLogger!.Object);

            // Assert
            Assert.IsNotNull(composite);
        }

        /// <summary>
        /// Tests that the constructor throws when receivers is null.
        /// </summary>
        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_ShouldThrowArgumentNullException_WhenReceiversIsNull()
        {
            // Act
            new CompositeNotificationReceiver(null!, _mockLogger!.Object);
        }

        /// <summary>
        /// Tests that the constructor throws when logger is null.
        /// </summary>
        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_ShouldThrowArgumentNullException_WhenLoggerIsNull()
        {
            // Arrange
            var receivers = new List<INotificationReceiver>();

            // Act
            new CompositeNotificationReceiver(receivers, null!);
        }

        /// <summary>
        /// Tests that the constructor accepts custom deduplication TTL.
        /// </summary>
        [TestMethod]
        public void Constructor_ShouldAcceptCustomDeduplicationTtl()
        {
            // Arrange
            var receivers = new List<INotificationReceiver>();
            var customTtl = TimeSpan.FromMinutes(5);

            // Act
            var composite = new CompositeNotificationReceiver(receivers, _mockLogger!.Object, customTtl);

            // Assert
            Assert.IsNotNull(composite);
        }

        #endregion

        #region Deduplication Tests (CRITICAL)

        /// <summary>
        /// ⭐ MOST IMPORTANT TEST ⭐
        /// Tests that the composite deduplicates notifications with the same ID from different transports.
        /// This is the core responsibility of CompositeNotificationReceiver.
        /// </summary>
        [TestMethod]
        public async Task Receive_ShouldDeduplicateAcrossTransports()
        {
            // Arrange
            var notificationId = Guid.NewGuid().ToString();
            var entity1 = new TestNotificationEntity { NotificationId = notificationId, Message = "From Transport 1" };
            var entity2 = new TestNotificationEntity { NotificationId = notificationId, Message = "From Transport 2" };

            // Create two subjects simulating two different transports
            var subject1 = new Subject<INotificationEntity>();
            var subject2 = new Subject<INotificationEntity>();

            var receiver1 = CreateMockReceiver(subject1);
            var receiver2 = CreateMockReceiver(subject2);

            var receivers = new List<INotificationReceiver> { receiver1, receiver2 };
            var composite = new CompositeNotificationReceiver(receivers, _mockLogger!.Object);

            var receivedCount = 0;
            var receivedMessages = new List<string>();
            using var cts = new CancellationTokenSource();

            // Subscribe to composite
            composite.Receive(cts.Token).Subscribe(e =>
            {
                receivedCount++;
                receivedMessages.Add(((TestNotificationEntity)e).Message);
            });

            // Act: Emit the same notification ID from both transports
            subject1.OnNext(entity1);
            await Task.Delay(50);
            subject2.OnNext(entity2);
            await Task.Delay(50);

            // Assert: Should only receive ONCE despite coming from 2 transports
            Assert.AreEqual(1, receivedCount,
                "CRITICAL FAILURE: Deduplication failed! Same notification was delivered multiple times.");
            Assert.AreEqual(1, receivedMessages.Count);
            // First one wins
            Assert.AreEqual("From Transport 1", receivedMessages[0]);
        }

        /// <summary>
        /// Tests that different notification IDs are NOT deduplicated.
        /// </summary>
        [TestMethod]
        public async Task Receive_ShouldNotDeduplicate_WhenDifferentIds()
        {
            // Arrange
            var entity1 = new TestNotificationEntity { NotificationId = Guid.NewGuid().ToString(), Message = "Message 1" };
            var entity2 = new TestNotificationEntity { NotificationId = Guid.NewGuid().ToString(), Message = "Message 2" };
            var entity3 = new TestNotificationEntity { NotificationId = Guid.NewGuid().ToString(), Message = "Message 3" };

            var subject = new Subject<INotificationEntity>();
            var receiver = CreateMockReceiver(subject);

            var receivers = new List<INotificationReceiver> { receiver };
            var composite = new CompositeNotificationReceiver(receivers, _mockLogger!.Object);

            var receivedCount = 0;
            using var cts = new CancellationTokenSource();

            composite.Receive(cts.Token).Subscribe(_ => receivedCount++);

            // Act: Emit three different notifications
            subject.OnNext(entity1);
            subject.OnNext(entity2);
            subject.OnNext(entity3);
            await Task.Delay(100);

            // Assert: All three should be received
            Assert.AreEqual(3, receivedCount);
        }

        /// <summary>
        /// Tests that deduplication works even when same ID comes from same transport multiple times.
        /// </summary>
        [TestMethod]
        public async Task Receive_ShouldDeduplicateFromSameTransport()
        {
            // Arrange
            var notificationId = Guid.NewGuid().ToString();
            var entity1 = new TestNotificationEntity { NotificationId = notificationId, Message = "First" };
            var entity2 = new TestNotificationEntity { NotificationId = notificationId, Message = "Duplicate" };

            var subject = new Subject<INotificationEntity>();
            var receiver = CreateMockReceiver(subject);

            var receivers = new List<INotificationReceiver> { receiver };
            var composite = new CompositeNotificationReceiver(receivers, _mockLogger!.Object);

            var receivedCount = 0;
            using var cts = new CancellationTokenSource();

            composite.Receive(cts.Token).Subscribe(_ => receivedCount++);

            // Act: Emit same ID twice from same transport
            subject.OnNext(entity1);
            await Task.Delay(50);
            subject.OnNext(entity2);
            await Task.Delay(50);

            // Assert: Should only receive once
            Assert.AreEqual(1, receivedCount);
        }

        /// <summary>
        /// Tests that deduplication cache respects TTL.
        /// After TTL expires, same ID can be received again.
        /// </summary>
        [TestMethod]
        public async Task Receive_ShouldRespectDeduplicationTtl()
        {
            // Arrange
            var notificationId = Guid.NewGuid().ToString();
            var entity1 = new TestNotificationEntity { NotificationId = notificationId, Message = "First" };
            var entity2 = new TestNotificationEntity { NotificationId = notificationId, Message = "After TTL" };

            var subject = new Subject<INotificationEntity>();
            var receiver = CreateMockReceiver(subject);

            var receivers = new List<INotificationReceiver> { receiver };
            var shortTtl = TimeSpan.FromMilliseconds(200); // Very short TTL for testing
            var composite = new CompositeNotificationReceiver(receivers, _mockLogger!.Object, shortTtl);

            var receivedCount = 0;
            using var cts = new CancellationTokenSource();

            composite.Receive(cts.Token).Subscribe(_ => receivedCount++);

            // Act: Emit, wait for TTL to expire, emit again
            subject.OnNext(entity1);
            await Task.Delay(50);
            Assert.AreEqual(1, receivedCount, "First emission should be received");

            // Wait for cache TTL to expire
            await Task.Delay(300);

            subject.OnNext(entity2);
            await Task.Delay(50);

            // Assert: Should receive twice (after TTL expired)
            Assert.AreEqual(2, receivedCount, "After TTL expiration, same ID should be accepted again");
        }

        #endregion

        #region Stream Merging Tests

        /// <summary>
        /// Tests that Receive merges streams from all receivers.
        /// </summary>
        [TestMethod]
        public async Task Receive_ShouldMergeStreamsFromAllReceivers()
        {
            // Arrange
            var subject1 = new Subject<INotificationEntity>();
            var subject2 = new Subject<INotificationEntity>();
            var subject3 = new Subject<INotificationEntity>();

            var receiver1 = CreateMockReceiver(subject1);
            var receiver2 = CreateMockReceiver(subject2);
            var receiver3 = CreateMockReceiver(subject3);

            var receivers = new List<INotificationReceiver> { receiver1, receiver2, receiver3 };
            var composite = new CompositeNotificationReceiver(receivers, _mockLogger!.Object);

            var receivedFromReceiver1 = false;
            var receivedFromReceiver2 = false;
            var receivedFromReceiver3 = false;
            using var cts = new CancellationTokenSource();

            composite.Receive(cts.Token).Subscribe(e =>
            {
                var msg = ((TestNotificationEntity)e).Message;
                if (msg == "From R1") receivedFromReceiver1 = true;
                if (msg == "From R2") receivedFromReceiver2 = true;
                if (msg == "From R3") receivedFromReceiver3 = true;
            });

            // Act: Emit from each receiver
            subject1.OnNext(new TestNotificationEntity { NotificationId = Guid.NewGuid().ToString(), Message = "From R1" });
            subject2.OnNext(new TestNotificationEntity { NotificationId = Guid.NewGuid().ToString(), Message = "From R2" });
            subject3.OnNext(new TestNotificationEntity { NotificationId = Guid.NewGuid().ToString(), Message = "From R3" });
            await Task.Delay(100);

            // Assert: Should receive from all receivers
            Assert.IsTrue(receivedFromReceiver1);
            Assert.IsTrue(receivedFromReceiver2);
            Assert.IsTrue(receivedFromReceiver3);
        }

        /// <summary>
        /// Tests that Receive handles receiver failures gracefully.
        /// </summary>
        [TestMethod]
        public async Task Receive_ShouldHandleReceiverFailures()
        {
            // Arrange
            var goodSubject = new Subject<INotificationEntity>();
            var goodReceiver = CreateMockReceiver(goodSubject);

            var failingReceiver = new Mock<INotificationReceiver>();
            failingReceiver.Setup(r => r.Receive(It.IsAny<CancellationToken>(), It.IsAny<string[]>()))
                .Throws(new InvalidOperationException("Receiver failed"));

            var receivers = new List<INotificationReceiver> { failingReceiver.Object, goodReceiver };
            var composite = new CompositeNotificationReceiver(receivers, _mockLogger!.Object);

            var receivedCount = 0;
            using var cts = new CancellationTokenSource();

            composite.Receive(cts.Token).Subscribe(_ => receivedCount++);

            // Act: Emit from good receiver
            goodSubject.OnNext(new TestNotificationEntity { NotificationId = Guid.NewGuid().ToString(), Message = "Test" });
            await Task.Delay(100);

            // Assert: Should still receive from good receiver despite failing receiver
            Assert.AreEqual(1, receivedCount);
        }

        #endregion

        #region Tag Filtering Tests

        /// <summary>
        /// Tests that Receive applies tag filtering via receivers (receivers filter, not composite).
        /// </summary>
        [TestMethod]
        public async Task Receive_ShouldFilterByTags_ViaReceivers()
        {
            // Arrange
            var subject = new Subject<INotificationEntity>();

            // Create a mock receiver that actually filters by tags (simulating InMemoryNotificationReceiver)
            var receiver = new Mock<INotificationReceiver>();
            receiver.Setup(r => r.Receive(It.IsAny<CancellationToken>(), It.IsAny<string[]>()))
                .Returns<CancellationToken, string[]>((ct, tags) =>
                {
                    var tagsSet = tags.ToHashSet();
                    return subject.AsObservable().Where(n =>
                        tagsSet.Count == 0 ||
                        tagsSet.Any(t => n.Tags?.Contains(t) ?? false)
                    );
                });

            var receivers = new List<INotificationReceiver> { receiver.Object };
            var composite = new CompositeNotificationReceiver(receivers, _mockLogger!.Object);

            var receivedCount = 0;
            using var cts = new CancellationTokenSource();

            // Subscribe with tag filter
            composite.Receive(cts.Token, "important").Subscribe(_ => receivedCount++);

            // Act
            subject.OnNext(new TestNotificationEntity
            {
                NotificationId = Guid.NewGuid().ToString(),
                Message = "Important",
                Tags = new HashSet<string> { "important" }
            });

            subject.OnNext(new TestNotificationEntity
            {
                NotificationId = Guid.NewGuid().ToString(),
                Message = "Not Important",
                Tags = new HashSet<string> { "info" }
            });

            await Task.Delay(100);

            // Assert: Should only receive the "important" tagged notification
            Assert.AreEqual(1, receivedCount);
        }

        #endregion

        #region Type Filtering Tests

        /// <summary>
        /// Tests that generic Receive method filters by type correctly.
        /// </summary>
        [TestMethod]
        public async Task Receive_Generic_ShouldFilterByType()
        {
            // Arrange
            var subject = new Subject<INotificationEntity>();
            var receiver = CreateMockReceiver(subject);

            var receivers = new List<INotificationReceiver> { receiver };
            var composite = new CompositeNotificationReceiver(receivers, _mockLogger!.Object);

            var receivedCount = 0;
            using var cts = new CancellationTokenSource();

            // Subscribe to specific type
            composite.Receive<TestNotificationEntity>(cts.Token).Subscribe(_ => receivedCount++);

            // Act
            subject.OnNext(new TestNotificationEntity { NotificationId = Guid.NewGuid().ToString(), Message = "Test" });
            subject.OnNext(new AnotherTestNotificationEntity { NotificationId = Guid.NewGuid().ToString(), Data = "Other" });
            await Task.Delay(100);

            // Assert: Should only receive TestNotificationEntity
            Assert.AreEqual(1, receivedCount);
        }

        #endregion

        #region Dispose Tests

        /// <summary>
        /// Tests that Dispose disposes all receivers and cache.
        /// </summary>
        [TestMethod]
        public void Dispose_ShouldDisposeAllReceivers()
        {
            // Arrange
            var receiver1 = new Mock<INotificationReceiver>();
            var disposableReceiver1 = receiver1.As<IDisposable>();

            var receiver2 = new Mock<INotificationReceiver>();
            var disposableReceiver2 = receiver2.As<IDisposable>();

            var receivers = new List<INotificationReceiver> { receiver1.Object, receiver2.Object };
            var composite = new CompositeNotificationReceiver(receivers, _mockLogger!.Object);

            // Act
            composite.Dispose();

            // Assert
            disposableReceiver1.Verify(d => d.Dispose(), Times.Once);
            disposableReceiver2.Verify(d => d.Dispose(), Times.Once);
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Creates a mock receiver that returns the given subject as observable.
        /// </summary>
        private INotificationReceiver CreateMockReceiver(Subject<INotificationEntity> subject)
        {
            var receiver = new Mock<INotificationReceiver>();
            receiver.Setup(r => r.Receive(It.IsAny<CancellationToken>(), It.IsAny<string[]>()))
                .Returns(subject);
            return receiver.Object;
        }

        #endregion

        #region Cache Size Limit Tests

        /// <summary>
        /// Tests that cache respects size limit configuration.
        /// </summary>
        [TestMethod]
        public async Task Receive_ShouldRespectCacheSizeLimit()
        {
            // Arrange - Create receiver with small cache size
            var subject = new Subject<INotificationEntity>();
            var receiver = CreateMockReceiver(subject);
            var composite = new CompositeNotificationReceiver(
                new List<INotificationReceiver> { receiver },
                _mockLogger!.Object,
                deduplicationTtl: TimeSpan.FromMinutes(10),
                maxCacheSize: 5 // Very small cache for testing
            );

            var receivedIds = new List<string>();
            using var cts = new CancellationTokenSource();

            var subscription = composite.Receive(cts.Token)
                .Subscribe(e => receivedIds.Add(e.NotificationId));

            // Act - Send more notifications than cache size
            for (int i = 0; i < 10; i++)
            {
                subject.OnNext(new TestNotificationEntity
                {
                    NotificationId = Guid.NewGuid().ToString(),
                    Message = $"Test {i}"
                });
                await Task.Delay(50); // Small delay to allow processing
            }

            await Task.Delay(200); // Wait for all to process

            // Assert - All notifications should be received (cache compaction should allow this)
            Assert.AreEqual(10, receivedIds.Count, "All notifications should be received despite small cache");
            Assert.AreEqual(10, receivedIds.Distinct().Count(), "All IDs should be unique");

            subscription.Dispose();
        }

        /// <summary>
        /// Tests that duplicate detection still works even with cache size limits.
        /// </summary>
        [TestMethod]
        public async Task Receive_ShouldDeduplicateEvenWithSmallCache()
        {
            // Arrange
            var subject = new Subject<INotificationEntity>();
            var receiver = CreateMockReceiver(subject);
            var composite = new CompositeNotificationReceiver(
                new List<INotificationReceiver> { receiver },
                _mockLogger!.Object,
                deduplicationTtl: TimeSpan.FromMinutes(10),
                maxCacheSize: 100
            );

            var receivedCount = 0;
            using var cts = new CancellationTokenSource();

            var subscription = composite.Receive(cts.Token)
                .Subscribe(e => receivedCount++);

            var notificationId = Guid.NewGuid().ToString();

            // Act - Send same notification twice in quick succession (should be in cache)
            subject.OnNext(new TestNotificationEntity { NotificationId = notificationId, Message = "Test 1" });
            await Task.Delay(50);
            subject.OnNext(new TestNotificationEntity { NotificationId = notificationId, Message = "Test 2" });
            await Task.Delay(100);

            // Assert - Should only receive once (duplicate filtered)
            Assert.AreEqual(1, receivedCount, "Duplicate should be filtered even with cache limits");

            subscription.Dispose();
        }

        /// <summary>
        /// Tests cache behavior with configured custom size.
        /// </summary>
        [TestMethod]
        public async Task Receive_ShouldHandleLargeCacheSize()
        {
            // Arrange - Large cache size
            var subject = new Subject<INotificationEntity>();
            var receiver = CreateMockReceiver(subject);
            var composite = new CompositeNotificationReceiver(
                new List<INotificationReceiver> { receiver },
                _mockLogger!.Object,
                deduplicationTtl: TimeSpan.FromMinutes(10),
                maxCacheSize: 50_000 // Large cache
            );

            var receivedCount = 0;
            using var cts = new CancellationTokenSource();

            var subscription = composite.Receive(cts.Token)
                .Subscribe(e => receivedCount++);

            // Act - Send many unique notifications
            for (int i = 0; i < 100; i++)
            {
                subject.OnNext(new TestNotificationEntity
                {
                    NotificationId = Guid.NewGuid().ToString(),
                    Message = $"Test {i}"
                });
            }

            await Task.Delay(200);

            // Assert - All should be received with large cache
            Assert.AreEqual(100, receivedCount, "All notifications should be received with large cache");

            subscription.Dispose();
        }

        #endregion
    }
}
