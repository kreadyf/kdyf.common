using kdyf.Notifications.Services;
using kdyf.Notifications.Test.Models;
using System.Reactive.Subjects;

namespace kdyf.Notifications.Test.Unit
{
    /// <summary>
    /// Unit tests for the <see cref="InMemoryNotificationEmitter"/> class.
    /// </summary>
    [TestClass]
    public sealed class InMemoryNotificationEmitterTests
    {
        private Subject<kdyf.Notifications.Interfaces.INotificationEntity>? _subject;
        private InMemoryNotificationEmitter? _emitter;

        [TestInitialize]
        public void Setup()
        {
            _subject = new Subject<kdyf.Notifications.Interfaces.INotificationEntity>();
            _emitter = new InMemoryNotificationEmitter(_subject);
        }

        [TestCleanup]
        public void Cleanup()
        {
            _emitter?.Dispose();
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
            new InMemoryNotificationEmitter(null!);
        }

        #endregion

        #region NotifyAsync Tests

        /// <summary>
        /// Tests that NotifyAsync emits notification to subject.
        /// </summary>
        [TestMethod]
        public async Task NotifyAsync_ShouldEmitToSubject()
        {
            // Arrange
            var entity = new TestNotificationEntity { Message = "Test" };
            kdyf.Notifications.Interfaces.INotificationEntity? received = null;

            _subject!.Subscribe(e => received = e);

            // Act
            await _emitter!.NotifyAsync(entity);
            await Task.Delay(50);

            // Assert
            Assert.IsNotNull(received);
            Assert.AreEqual(entity.Message, ((TestNotificationEntity)received).Message);
        }

        /// <summary>
        /// ⭐ CRITICAL: Tests that NotifyAsync does NOT deduplicate.
        /// Deduplication is the responsibility of CompositeNotificationReceiver only.
        /// </summary>
        [TestMethod]
        public async Task NotifyAsync_ShouldNotDeduplicate()
        {
            // Arrange
            var notificationId = Guid.NewGuid().ToString();
            var entity1 = new TestNotificationEntity { NotificationId = notificationId, Message = "First" };
            var entity2 = new TestNotificationEntity { NotificationId = notificationId, Message = "Duplicate" };

            var receivedCount = 0;
            _subject!.Subscribe(_ => receivedCount++);

            // Act: Emit same notification ID twice
            await _emitter!.NotifyAsync(entity1);
            await _emitter!.NotifyAsync(entity2);
            await Task.Delay(50);

            // Assert: Should receive BOTH (no deduplication)
            Assert.AreEqual(2, receivedCount,
                "InMemoryEmitter should NOT deduplicate - that's CompositeReceiver's job!");
        }

        /// <summary>
        /// Tests that NotifyAsync preserves timestamp if already set.
        /// Note: Timestamp is set by CompositeNotificationEmitter before reaching InMemoryNotificationEmitter.
        /// </summary>
        [TestMethod]
        public async Task NotifyAsync_ShouldPreserveTimestamp_WhenAlreadySet()
        {
            // Arrange
            var customTimestamp = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            var entity = new TestNotificationEntity { Message = "Test", Timestamp = customTimestamp };
            kdyf.Notifications.Interfaces.INotificationEntity? received = null;

            _subject!.Subscribe(e => received = e);

            // Act
            await _emitter!.NotifyAsync(entity);
            await Task.Delay(50);

            // Assert
            Assert.IsNotNull(received);
            Assert.AreEqual(customTimestamp, received.Timestamp);
        }

        /// <summary>
        /// Tests that NotifyAsync throws when entity is null.
        /// </summary>
        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public async Task NotifyAsync_ShouldThrowArgumentNullException_WhenEntityIsNull()
        {
            // Act
            await _emitter!.NotifyAsync<TestNotificationEntity>(null!);
        }

        /// <summary>
        /// Tests that NotifyAsync respects cancellation token.
        /// </summary>
        [TestMethod]
        [ExpectedException(typeof(OperationCanceledException))]
        public async Task NotifyAsync_ShouldRespectCancellationToken()
        {
            // Arrange
            var entity = new TestNotificationEntity { Message = "Test" };
            var cts = new CancellationTokenSource();
            cts.Cancel(); // Cancel immediately

            // Act
            await _emitter!.NotifyAsync(entity, cts.Token);
        }

        #endregion

        #region Thread Safety Tests

        /// <summary>
        /// Tests that NotifyAsync is thread-safe.
        /// </summary>
        [TestMethod]
        public async Task NotifyAsync_ShouldBeThreadSafe()
        {
            // Arrange
            var receivedCount = 0;
            var expectedCount = 100;

            _subject!.Subscribe(_ => Interlocked.Increment(ref receivedCount));

            // Act: Emit from multiple threads
            var tasks = Enumerable.Range(0, expectedCount)
                .Select(i => Task.Run(async () =>
                {
                    await _emitter!.NotifyAsync(new TestNotificationEntity
                    {
                        NotificationId = Guid.NewGuid().ToString(),
                        Message = $"Message {i}"
                    });
                }))
                .ToArray();

            await Task.WhenAll(tasks);
            await Task.Delay(200);

            // Assert
            Assert.AreEqual(expectedCount, receivedCount);
        }

        #endregion

        #region Dispose Tests

        /// <summary>
        /// Tests that Dispose throws ObjectDisposedException on subsequent calls.
        /// </summary>
        [TestMethod]
        [ExpectedException(typeof(ObjectDisposedException))]
        public async Task NotifyAsync_ShouldThrowObjectDisposedException_AfterDispose()
        {
            // Arrange
            var entity = new TestNotificationEntity { Message = "Test" };

            // Act
            _emitter!.Dispose();
            await _emitter.NotifyAsync(entity);
        }

        /// <summary>
        /// Tests that Dispose can be called multiple times safely.
        /// </summary>
        [TestMethod]
        public void Dispose_ShouldBeIdempotent()
        {
            // Act & Assert: Should not throw
            _emitter!.Dispose();
            _emitter.Dispose();
            _emitter.Dispose();
        }

        #endregion
    }
}
