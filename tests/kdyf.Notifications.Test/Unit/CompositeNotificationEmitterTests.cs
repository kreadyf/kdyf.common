using kdyf.Notifications.Interfaces;
using kdyf.Notifications.Services;
using kdyf.Notifications.Test.Models;
using Microsoft.Extensions.Logging;
using Moq;

namespace kdyf.Notifications.Test.Unit
{
    /// <summary>
    /// Unit tests for the <see cref="CompositeNotificationEmitter"/> class.
    /// </summary>
    [TestClass]
    public sealed class CompositeNotificationEmitterTests
    {
        private Mock<ILogger<CompositeNotificationEmitter>>? _mockLogger;

        [TestInitialize]
        public void Setup()
        {
            _mockLogger = new Mock<ILogger<CompositeNotificationEmitter>>();
        }

        #region Constructor Tests

        /// <summary>
        /// Tests that the constructor initializes with multiple emitters.
        /// </summary>
        [TestMethod]
        public void Constructor_ShouldAcceptMultipleEmitters()
        {
            // Arrange
            var emitter1 = new Mock<INotificationEmitter>();
            var emitter2 = new Mock<INotificationEmitter>();
            var emitters = new List<INotificationEmitter> { emitter1.Object, emitter2.Object };

            // Act
            var composite = new CompositeNotificationEmitter(emitters, _mockLogger!.Object);

            // Assert
            Assert.IsNotNull(composite);
        }

        /// <summary>
        /// Tests that the constructor throws when emitters is null.
        /// </summary>
        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_ShouldThrowArgumentNullException_WhenEmittersIsNull()
        {
            // Act
            new CompositeNotificationEmitter(null!, _mockLogger!.Object);
        }

        /// <summary>
        /// Tests that the constructor throws when logger is null.
        /// </summary>
        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_ShouldThrowArgumentNullException_WhenLoggerIsNull()
        {
            // Arrange
            var emitters = new List<INotificationEmitter>();

            // Act
            new CompositeNotificationEmitter(emitters, null!);
        }

        /// <summary>
        /// Tests that the constructor logs when initialized with no emitters.
        /// </summary>
        [TestMethod]
        public void Constructor_ShouldLogWarning_WhenNoEmittersProvided()
        {
            // Arrange
            var emitters = new List<INotificationEmitter>();

            // Act
            var composite = new CompositeNotificationEmitter(emitters, _mockLogger!.Object);

            // Assert
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("no emitters")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        #endregion

        #region NotifyAsync Tests

        /// <summary>
        /// Tests that NotifyAsync emits to all registered emitters.
        /// </summary>
        [TestMethod]
        public async Task NotifyAsync_ShouldEmitToAllEmitters()
        {
            // Arrange
            var entity = new TestNotificationEntity { Message = "Test" };
            var emitter1 = new Mock<INotificationEmitter>();
            var emitter2 = new Mock<INotificationEmitter>();
            var emitter3 = new Mock<INotificationEmitter>();
            var emitters = new List<INotificationEmitter> { emitter1.Object, emitter2.Object, emitter3.Object };

            var composite = new CompositeNotificationEmitter(emitters, _mockLogger!.Object);

            // Act
            await composite.NotifyAsync(entity);

            // Assert
            emitter1.Verify(e => e.NotifyAsync(entity, It.IsAny<CancellationToken>()), Times.Once);
            emitter2.Verify(e => e.NotifyAsync(entity, It.IsAny<CancellationToken>()), Times.Once);
            emitter3.Verify(e => e.NotifyAsync(entity, It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// Tests that NotifyAsync emits to all emitters in parallel.
        /// Verifies parallel execution by checking concurrent invocations rather than timing.
        /// </summary>
        [TestMethod]
        public async Task NotifyAsync_ShouldEmitInParallel()
        {
            // Arrange
            var entity = new TestNotificationEntity { Message = "Test" };
            var concurrentCalls = 0;
            var maxConcurrentCalls = 0;
            var lockObj = new object();

            var emitter1 = new Mock<INotificationEmitter>();
            emitter1.Setup(e => e.NotifyAsync(It.IsAny<TestNotificationEntity>(), It.IsAny<CancellationToken>()))
                .Returns(async () =>
                {
                    lock (lockObj)
                    {
                        concurrentCalls++;
                        if (concurrentCalls > maxConcurrentCalls)
                            maxConcurrentCalls = concurrentCalls;
                    }

                    await Task.Delay(50);

                    lock (lockObj)
                    {
                        concurrentCalls--;
                    }
                });

            var emitter2 = new Mock<INotificationEmitter>();
            emitter2.Setup(e => e.NotifyAsync(It.IsAny<TestNotificationEntity>(), It.IsAny<CancellationToken>()))
                .Returns(async () =>
                {
                    lock (lockObj)
                    {
                        concurrentCalls++;
                        if (concurrentCalls > maxConcurrentCalls)
                            maxConcurrentCalls = concurrentCalls;
                    }

                    await Task.Delay(50);

                    lock (lockObj)
                    {
                        concurrentCalls--;
                    }
                });

            var emitters = new List<INotificationEmitter> { emitter1.Object, emitter2.Object };
            var composite = new CompositeNotificationEmitter(emitters, _mockLogger!.Object);

            // Act
            await composite.NotifyAsync(entity);

            // Assert: If parallel, both emitters should have been running at the same time
            // maxConcurrentCalls should be 2 (both emitters running concurrently)
            Assert.AreEqual(2, maxConcurrentCalls,
                "Expected both emitters to run concurrently (parallel execution)");

            // Verify both emitters were called
            emitter1.Verify(e => e.NotifyAsync(entity, It.IsAny<CancellationToken>()), Times.Once);
            emitter2.Verify(e => e.NotifyAsync(entity, It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// Tests that NotifyAsync continues when one emitter fails.
        /// </summary>
        [TestMethod]
        public async Task NotifyAsync_ShouldContinueOnTransportFailure()
        {
            // Arrange
            var entity = new TestNotificationEntity { Message = "Test" };

            var emitter1 = new Mock<INotificationEmitter>();
            emitter1.Setup(e => e.NotifyAsync(It.IsAny<TestNotificationEntity>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("Emitter 1 failed"));

            var emitter2 = new Mock<INotificationEmitter>();
            var emitter3 = new Mock<INotificationEmitter>();

            var emitters = new List<INotificationEmitter> { emitter1.Object, emitter2.Object, emitter3.Object };
            var composite = new CompositeNotificationEmitter(emitters, _mockLogger!.Object);

            // Act
            await composite.NotifyAsync(entity);

            // Assert: All emitters should be called, even after failure
            emitter1.Verify(e => e.NotifyAsync(entity, It.IsAny<CancellationToken>()), Times.Once);
            emitter2.Verify(e => e.NotifyAsync(entity, It.IsAny<CancellationToken>()), Times.Once);
            emitter3.Verify(e => e.NotifyAsync(entity, It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// Tests that NotifyAsync logs failures.
        /// </summary>
        [TestMethod]
        public async Task NotifyAsync_ShouldLogFailures()
        {
            // Arrange
            var entity = new TestNotificationEntity { Message = "Test" };
            var exception = new InvalidOperationException("Test failure");

            var failingEmitter = new Mock<INotificationEmitter>();
            failingEmitter.Setup(e => e.NotifyAsync(It.IsAny<TestNotificationEntity>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(exception);

            var emitters = new List<INotificationEmitter> { failingEmitter.Object };
            var composite = new CompositeNotificationEmitter(emitters, _mockLogger!.Object);

            // Act
            await composite.NotifyAsync(entity);

            // Assert
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Failed to emit")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        /// <summary>
        /// Tests that NotifyAsync throws when entity is null.
        /// </summary>
        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public async Task NotifyAsync_ShouldThrowArgumentNullException_WhenEntityIsNull()
        {
            // Arrange
            var emitters = new List<INotificationEmitter>();
            var composite = new CompositeNotificationEmitter(emitters, _mockLogger!.Object);

            // Act
            await composite.NotifyAsync<TestNotificationEntity>(null!);
        }

        /// <summary>
        /// Tests that NotifyAsync respects cancellation token.
        /// </summary>
        [TestMethod]
        public async Task NotifyAsync_ShouldRespectCancellationToken()
        {
            // Arrange
            var entity = new TestNotificationEntity { Message = "Test" };
            var cts = new CancellationTokenSource();
            cts.Cancel(); // Cancel immediately

            var emitter = new Mock<INotificationEmitter>();
            emitter.Setup(e => e.NotifyAsync(It.IsAny<TestNotificationEntity>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new OperationCanceledException());

            var emitters = new List<INotificationEmitter> { emitter.Object };
            var composite = new CompositeNotificationEmitter(emitters, _mockLogger!.Object);

            // Act & Assert
            await Assert.ThrowsExceptionAsync<OperationCanceledException>(
                async () => await composite.NotifyAsync(entity, cts.Token));
        }

        #endregion

        #region Dispose Tests

        /// <summary>
        /// Tests that Dispose disposes all emitters.
        /// </summary>
        [TestMethod]
        public void Dispose_ShouldDisposeAllEmitters()
        {
            // Arrange
            var emitter1 = new Mock<INotificationEmitter>();
            var disposableEmitter1 = emitter1.As<IDisposable>();

            var emitter2 = new Mock<INotificationEmitter>();
            var disposableEmitter2 = emitter2.As<IDisposable>();

            var emitters = new List<INotificationEmitter> { emitter1.Object, emitter2.Object };
            var composite = new CompositeNotificationEmitter(emitters, _mockLogger!.Object);

            // Act
            composite.Dispose();

            // Assert
            disposableEmitter1.Verify(d => d.Dispose(), Times.Once);
            disposableEmitter2.Verify(d => d.Dispose(), Times.Once);
        }

        /// <summary>
        /// Tests that Dispose can be called multiple times safely.
        /// </summary>
        [TestMethod]
        public void Dispose_ShouldBeIdempotent()
        {
            // Arrange
            var emitter = new Mock<INotificationEmitter>();
            var disposableEmitter = emitter.As<IDisposable>();
            var emitters = new List<INotificationEmitter> { emitter.Object };
            var composite = new CompositeNotificationEmitter(emitters, _mockLogger!.Object);

            // Act
            composite.Dispose();
            composite.Dispose();
            composite.Dispose();

            // Assert: Should only dispose once
            disposableEmitter.Verify(d => d.Dispose(), Times.Once);
        }

        #endregion
    }
}
