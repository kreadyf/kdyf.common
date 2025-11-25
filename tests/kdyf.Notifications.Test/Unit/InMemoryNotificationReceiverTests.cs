using kdyf.Notifications.Services;
using kdyf.Notifications.Test.Models;
using System.Reactive.Subjects;

namespace kdyf.Notifications.Test.Unit
{
    /// <summary>
    /// Unit tests for the <see cref="InMemoryNotificationReceiver"/> class.
    /// </summary>
    [TestClass]
    public sealed class InMemoryNotificationReceiverTests
    {
        private Subject<kdyf.Notifications.Interfaces.INotificationEntity>? _subject;
        private InMemoryNotificationReceiver? _receiver;

        [TestInitialize]
        public void Setup()
        {
            _subject = new Subject<kdyf.Notifications.Interfaces.INotificationEntity>();
            _receiver = new InMemoryNotificationReceiver(_subject);
        }

        [TestCleanup]
        public void Cleanup()
        {
            _receiver?.Dispose();
            _subject?.Dispose();
        }

        #region Constructor Tests

        /// <summary>
        /// Tests that constructor throws when subject is null.
        /// </summary>
        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_ShouldThrowArgumentNullException_WhenSubjectIsNull()
        {
            // Act
            new InMemoryNotificationReceiver(null!);
        }

        #endregion

        #region Receive Tests

        /// <summary>
        /// Tests that Receive observes the subject.
        /// </summary>
        [TestMethod]
        public async Task Receive_ShouldObserveSubject()
        {
            // Arrange
            var entity = new TestNotificationEntity { Message = "Test" };
            kdyf.Notifications.Interfaces.INotificationEntity? received = null;
            using var cts = new CancellationTokenSource();

            // Subscribe
            _receiver!.Receive(cts.Token).Subscribe(e => received = e);

            // Act: Emit to subject
            _subject!.OnNext(entity);
            await Task.Delay(50);

            // Assert
            Assert.IsNotNull(received);
            Assert.AreEqual(entity.Message, ((TestNotificationEntity)received).Message);
        }

        /// <summary>
        /// ⭐ CRITICAL: Tests that Receive does NOT deduplicate.
        /// Deduplication is the responsibility of CompositeNotificationReceiver only.
        /// </summary>
        [TestMethod]
        public async Task Receive_ShouldNotDeduplicate()
        {
            // Arrange
            var notificationId = Guid.NewGuid().ToString();
            var entity1 = new TestNotificationEntity { NotificationId = notificationId, Message = "First" };
            var entity2 = new TestNotificationEntity { NotificationId = notificationId, Message = "Duplicate" };

            var receivedCount = 0;
            using var cts = new CancellationTokenSource();

            _receiver!.Receive(cts.Token).Subscribe(_ => receivedCount++);

            // Act: Emit same notification ID twice
            _subject!.OnNext(entity1);
            _subject!.OnNext(entity2);
            await Task.Delay(50);

            // Assert: Should receive BOTH (no deduplication)
            Assert.AreEqual(2, receivedCount,
                "InMemoryReceiver should NOT deduplicate - that's CompositeReceiver's job!");
        }

        /// <summary>
        /// Tests that Receive receives all notifications when no tags specified.
        /// </summary>
        [TestMethod]
        public async Task Receive_ShouldReceiveAll_WhenNoTagsSpecified()
        {
            // Arrange
            var receivedCount = 0;
            using var cts = new CancellationTokenSource();

            _receiver!.Receive(cts.Token).Subscribe(_ => receivedCount++);

            // Act
            _subject!.OnNext(new TestNotificationEntity { NotificationId = Guid.NewGuid().ToString(), Message = "1" });
            _subject!.OnNext(new TestNotificationEntity { NotificationId = Guid.NewGuid().ToString(), Message = "2" });
            _subject!.OnNext(new TestNotificationEntity { NotificationId = Guid.NewGuid().ToString(), Message = "3" });
            await Task.Delay(100);

            // Assert
            Assert.AreEqual(3, receivedCount);
        }

        /// <summary>
        /// Tests that Receive filters by tags correctly.
        /// </summary>
        [TestMethod]
        public async Task Receive_ShouldFilterByTags()
        {
            // Arrange
            var receivedMessages = new List<string>();
            using var cts = new CancellationTokenSource();

            _receiver!.Receive(cts.Token, "important").Subscribe(e =>
                receivedMessages.Add(((TestNotificationEntity)e).Message));

            // Act
            _subject!.OnNext(new TestNotificationEntity
            {
                NotificationId = Guid.NewGuid().ToString(),
                Message = "Should receive",
                Tags = new HashSet<string> { "important" }
            });

            _subject!.OnNext(new TestNotificationEntity
            {
                NotificationId = Guid.NewGuid().ToString(),
                Message = "Should NOT receive",
                Tags = new HashSet<string> { "info" }
            });

            _subject!.OnNext(new TestNotificationEntity
            {
                NotificationId = Guid.NewGuid().ToString(),
                Message = "Should also receive",
                Tags = new HashSet<string> { "important", "urgent" }
            });

            await Task.Delay(100);

            // Assert
            Assert.AreEqual(2, receivedMessages.Count);
            Assert.IsTrue(receivedMessages.Contains("Should receive"));
            Assert.IsTrue(receivedMessages.Contains("Should also receive"));
            Assert.IsFalse(receivedMessages.Contains("Should NOT receive"));
        }

        /// <summary>
        /// Tests that Receive filters notifications with no tags when tags are specified.
        /// </summary>
        [TestMethod]
        public async Task Receive_ShouldNotReceive_WhenNotificationHasNoTags()
        {
            // Arrange
            var receivedCount = 0;
            using var cts = new CancellationTokenSource();

            _receiver!.Receive(cts.Token, "important").Subscribe(_ => receivedCount++);

            // Act: Emit notification with no tags
            _subject!.OnNext(new TestNotificationEntity
            {
                NotificationId = Guid.NewGuid().ToString(),
                Message = "No tags",
                Tags = new HashSet<string>()
            });

            await Task.Delay(100);

            // Assert
            Assert.AreEqual(0, receivedCount);
        }

        #endregion

        #region Generic Receive Tests

        /// <summary>
        /// Tests that generic Receive filters by type.
        /// </summary>
        [TestMethod]
        public async Task Receive_Generic_ShouldFilterByType()
        {
            // Arrange
            var receivedCount = 0;
            using var cts = new CancellationTokenSource();

            _receiver!.Receive<TestNotificationEntity>(cts.Token).Subscribe(_ => receivedCount++);

            // Act
            _subject!.OnNext(new TestNotificationEntity { NotificationId = Guid.NewGuid().ToString(), Message = "Test" });
            _subject!.OnNext(new AnotherTestNotificationEntity { NotificationId = Guid.NewGuid().ToString(), Data = "Other" });
            _subject!.OnNext(new TestNotificationEntity { NotificationId = Guid.NewGuid().ToString(), Message = "Test 2" });
            await Task.Delay(100);

            // Assert: Should only receive TestNotificationEntity
            Assert.AreEqual(2, receivedCount);
        }

        /// <summary>
        /// Tests that generic Receive filters by both type and tags.
        /// </summary>
        [TestMethod]
        public async Task Receive_Generic_ShouldFilterByTypeAndTags()
        {
            // Arrange
            var receivedCount = 0;
            using var cts = new CancellationTokenSource();

            _receiver!.Receive<TestNotificationEntity>(cts.Token, "important").Subscribe(_ => receivedCount++);

            // Act
            _subject!.OnNext(new TestNotificationEntity
            {
                NotificationId = Guid.NewGuid().ToString(),
                Message = "Important Test",
                Tags = new HashSet<string> { "important" }
            });

            _subject!.OnNext(new TestNotificationEntity
            {
                NotificationId = Guid.NewGuid().ToString(),
                Message = "Not Important Test",
                Tags = new HashSet<string> { "info" }
            });

            _subject!.OnNext(new AnotherTestNotificationEntity
            {
                NotificationId = Guid.NewGuid().ToString(),
                Data = "Important Other",
                Tags = new HashSet<string> { "important" }
            });

            await Task.Delay(100);

            // Assert: Should only receive TestNotificationEntity with "important" tag
            Assert.AreEqual(1, receivedCount);
        }

        #endregion

        #region Cancellation Tests

        /// <summary>
        /// Tests that Receive respects cancellation token.
        /// </summary>
        [TestMethod]
        public async Task Receive_ShouldRespectCancellation()
        {
            // Arrange
            var receivedCount = 0;
            using var cts = new CancellationTokenSource();

            _receiver!.Receive(cts.Token).Subscribe(_ => receivedCount++);

            // Act
            _subject!.OnNext(new TestNotificationEntity { NotificationId = Guid.NewGuid().ToString(), Message = "Before" });
            await Task.Delay(50);

            cts.Cancel();
            await Task.Delay(50);

            _subject!.OnNext(new TestNotificationEntity { NotificationId = Guid.NewGuid().ToString(), Message = "After" });
            await Task.Delay(50);

            // Assert: Should only receive the first one
            Assert.AreEqual(1, receivedCount);
        }

        #endregion

        #region Multiple Subscribers Tests

        /// <summary>
        /// Tests that multiple subscribers receive the same notification.
        /// </summary>
        [TestMethod]
        public async Task Receive_ShouldSupportMultipleSubscribers()
        {
            // Arrange
            var received1 = false;
            var received2 = false;
            var received3 = false;
            using var cts = new CancellationTokenSource();

            _receiver!.Receive(cts.Token).Subscribe(_ => received1 = true);
            _receiver!.Receive(cts.Token).Subscribe(_ => received2 = true);
            _receiver!.Receive(cts.Token).Subscribe(_ => received3 = true);

            // Act
            _subject!.OnNext(new TestNotificationEntity { NotificationId = Guid.NewGuid().ToString(), Message = "Test" });
            await Task.Delay(100);

            // Assert
            Assert.IsTrue(received1);
            Assert.IsTrue(received2);
            Assert.IsTrue(received3);
        }

        #endregion

        #region Dispose Tests

        /// <summary>
        /// Tests that Receive throws ObjectDisposedException after disposal.
        /// </summary>
        [TestMethod]
        [ExpectedException(typeof(ObjectDisposedException))]
        public void Receive_ShouldThrowObjectDisposedException_AfterDispose()
        {
            // Arrange
            using var cts = new CancellationTokenSource();

            // Act
            _receiver!.Dispose();
            _receiver.Receive(cts.Token);
        }

        /// <summary>
        /// Tests that Dispose can be called multiple times safely.
        /// </summary>
        [TestMethod]
        public void Dispose_ShouldBeIdempotent()
        {
            // Act & Assert: Should not throw
            _receiver!.Dispose();
            _receiver.Dispose();
            _receiver.Dispose();
        }

        #endregion
    }
}
