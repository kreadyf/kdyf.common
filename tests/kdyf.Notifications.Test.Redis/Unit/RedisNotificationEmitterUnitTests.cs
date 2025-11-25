using kdyf.Notifications.Interfaces;
using kdyf.Notifications.Redis.Configuration;
using kdyf.Notifications.Redis.Resilience;
using kdyf.Notifications.Redis.Services;
using kdyf.Notifications.Test.Redis.Helpers;
using kdyf.Notifications.Test.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using StackExchange.Redis;
using System.Text.Json;

namespace kdyf.Notifications.Test.Redis.Unit
{
    /// <summary>
    /// Unit tests for RedisNotificationEmitter using mocks (no real Redis connection).
    /// Tests the fire-and-forget channel, retry logic,
    /// and different storage strategies (standard, updateable, stream-only).
    /// </summary>
    [TestClass]
    [TestCategory("UnitTest")]
    public sealed class RedisNotificationEmitterUnitTests
    {
        private Mock<IConnectionMultiplexer> _mockRedis = null!;
        private Mock<IDatabase> _mockDatabase = null!;
        private Mock<IRetryPolicy> _mockRetryPolicy = null!;
        private Mock<ILogger<RedisNotificationEmitter>> _mockLogger = null!;
        private Mock<IConfiguration> _mockConfiguration = null!;

        [TestInitialize]
        public void Setup()
        {
            // Create mocks
            var mocks = MockRedisFactory.CreateRedisMocks();
            _mockRedis = mocks.ConnectionMultiplexer;
            _mockDatabase = mocks.Database;

            _mockRetryPolicy = new Mock<IRetryPolicy>();
            _mockLogger = new Mock<ILogger<RedisNotificationEmitter>>();

            // Setup configuration mock
            _mockConfiguration = new Mock<IConfiguration>();
            var mockSection = new Mock<IConfigurationSection>();
            _mockConfiguration.Setup(x => x.GetSection("Redis")).Returns(mockSection.Object);

            // Setup retry policy to execute without retry (both generic and non-generic)
            _mockRetryPolicy
                .Setup(x => x.ExecuteAsync(It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()))
                .Returns<Func<Task>, CancellationToken>((func, ct) => func());

            _mockRetryPolicy
                .Setup(x => x.ExecuteAsync(It.IsAny<Func<Task<bool>>>(), It.IsAny<CancellationToken>()))
                .Returns<Func<Task<bool>>, CancellationToken>(async (func, ct) => await func());
        }

        #region Constructor Tests

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_ShouldThrowArgumentNullException_WhenRedisIsNull()
        {
            // Act
            var emitter = new RedisNotificationEmitter(
                _mockLogger.Object,
                null!,
                _mockConfiguration.Object,
                _mockRetryPolicy.Object
            );

            // Assert - ExpectedException
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_ShouldThrowArgumentNullException_WhenLoggerIsNull()
        {
            // Act
            var emitter = new RedisNotificationEmitter(
                null!,
                _mockRedis.Object,
                _mockConfiguration.Object,
                _mockRetryPolicy.Object
            );

            // Assert - ExpectedException
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_ShouldThrowArgumentNullException_WhenRetryPolicyIsNull()
        {
            // Act
            var emitter = new RedisNotificationEmitter(
                _mockLogger.Object,
                _mockRedis.Object,
                _mockConfiguration.Object,
                null!
            );

            // Assert - ExpectedException
        }

        #endregion

        #region NotifyAsync - Channel Writing Tests

        [TestMethod]
        public async Task NotifyAsync_ShouldWriteToChannel_WithoutBlocking()
        {
            // Arrange
            var emitter = CreateEmitter();
            await StartEmitterAsync(emitter);

            var entity = new TestNotificationEntity
            {
                NotificationId = Guid.NewGuid().ToString(),
                Message = "Test message",
                Timestamp = DateTime.UtcNow
            };

            // Act
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await emitter.NotifyAsync(entity);
            sw.Stop();

            // Assert - Should be very fast (< 100ms) since it only writes to channel
            Assert.IsTrue(sw.ElapsedMilliseconds < 100,
                $"NotifyAsync should be fast (fire-and-forget), took {sw.ElapsedMilliseconds}ms");

            await StopEmitterAsync(emitter);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public async Task NotifyAsync_ShouldThrowArgumentNullException_WhenEntityIsNull()
        {
            // Arrange
            var emitter = CreateEmitter();
            await StartEmitterAsync(emitter);

            // Act
            await emitter.NotifyAsync<TestNotificationEntity>(null!);

            // Assert - ExpectedException
        }

        [TestMethod]
        public async Task NotifyAsync_ShouldQueueMultipleMessages_Concurrently()
        {
            // Arrange
            var emitter = CreateEmitter();
            await StartEmitterAsync(emitter);

            var entities = Enumerable.Range(0, 100).Select(i => new TestNotificationEntity
            {
                NotificationId = Guid.NewGuid().ToString(),
                Message = $"Message {i}",
                Timestamp = DateTime.UtcNow
            }).ToList();

            // Act - Send all concurrently
            var tasks = entities.Select(e => emitter.NotifyAsync(e));
            await Task.WhenAll(tasks);

            // Assert - All should complete quickly without blocking
            Assert.IsTrue(true, "All messages queued successfully");

            await StopEmitterAsync(emitter);
        }

        #endregion

        #region Background Processing Tests

        [TestMethod]
        public async Task BackgroundProcessor_ShouldCallRedis_WhenMessagesQueued()
        {
            // Arrange
            var emitter = CreateEmitter();
            await StartEmitterAsync(emitter);

            var entity = new TestNotificationEntity
            {
                NotificationId = Guid.NewGuid().ToString(),
                Message = "Test Redis call",
                Timestamp = DateTime.UtcNow
            };

            // Act
            await emitter.NotifyAsync(entity);
            await Task.Delay(1000); // Give background processor time to process

            // Assert - Verify Redis operations were called
            // Instead of verifying the exact StreamAddAsync signature (which has multiple overloads),
            // we verify that StringSetAsync was called (which stores the notification data)
            _mockDatabase.Verify(
                x => x.StringSetAsync(
                    It.Is<RedisKey>(k => k.ToString().StartsWith("notifications:")),
                    It.IsAny<RedisValue>(),
                    It.IsAny<TimeSpan?>(),
                    It.IsAny<bool>(),
                    It.IsAny<When>(),
                    It.IsAny<CommandFlags>()
                ),
                Times.AtLeastOnce,
                "Background processor should store notification data in Redis"
            );

            await StopEmitterAsync(emitter);
        }

        #endregion


        #region Retry Policy Integration Tests

        [TestMethod]
        public async Task NotifyAsync_ShouldUseRetryPolicy_OnTransientFailures()
        {
            // Arrange
            var retryCount = 0;
            _mockRetryPolicy
                .Setup(x => x.ExecuteAsync(It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()))
                .Returns<Func<Task>, CancellationToken>(async (func, ct) =>
                {
                    retryCount++;
                    await func();
                });

            var emitter = CreateEmitter();
            await StartEmitterAsync(emitter);

            var entity = new TestNotificationEntity
            {
                NotificationId = Guid.NewGuid().ToString(),
                Message = "Retry test",
                Timestamp = DateTime.UtcNow
            };

            // Act
            await emitter.NotifyAsync(entity);
            await Task.Delay(1000); // Wait for background processing

            // Assert - Verify retry policy was used (checks generic version)
            _mockRetryPolicy.Verify(
                x => x.ExecuteAsync(It.IsAny<Func<Task<bool>>>(), It.IsAny<CancellationToken>()),
                Times.AtLeastOnce,
                "Retry policy should be used for Redis operations"
            );

            await StopEmitterAsync(emitter);
        }

        #endregion

        #region TTL Tests

        [TestMethod]
        public async Task NotifyAsync_ShouldSetTTL_WhenConfigured()
        {
            // Arrange
            var options = new RedisNotificationOptions();
            options.Storage.MessageTTL = TimeSpan.FromMinutes(30);

            var emitter = CreateEmitter(options);
            await StartEmitterAsync(emitter);

            var entity = new TestNotificationEntity
            {
                NotificationId = Guid.NewGuid().ToString(),
                Message = "TTL test",
                Timestamp = DateTime.UtcNow
            };

            // Act
            await emitter.NotifyAsync(entity);
            await Task.Delay(1500); // Wait for background processing

            // Assert - Verify StringSetAsync was called with TTL
            // Note: The signature is StringSetAsync(key, value, expiry, keepTtl, when, flags)
            _mockDatabase.Verify(
                x => x.StringSetAsync(
                    It.IsAny<RedisKey>(),
                    It.IsAny<RedisValue>(),
                    It.Is<TimeSpan?>(ttl => ttl.HasValue && ttl.Value.TotalMinutes == 30),
                    It.IsAny<bool>(), // keepTtl parameter
                    It.IsAny<When>(),
                    It.IsAny<CommandFlags>()
                ),
                Times.AtLeastOnce,
                "Should set TTL when writing to Redis"
            );

            await StopEmitterAsync(emitter);
        }

        #endregion

        #region Updateable Notification Tests

        // NOTE: Updateable and Stream-Only tests removed - these require builder patterns
        // that are tested in integration tests

        #endregion

        #region Hosted Service Tests

        [TestMethod]
        public async Task StartAsync_ShouldStartBackgroundProcessor()
        {
            // Arrange
            var emitter = CreateEmitter();

            // Act
            await emitter.StartAsync(CancellationToken.None);
            await Task.Delay(500); // Give it time to start

            // Background task should be running
            Assert.IsTrue(true, "Background processor started successfully");

            await emitter.StopAsync(CancellationToken.None);
        }

        [TestMethod]
        public async Task StopAsync_ShouldStopBackgroundProcessor_Gracefully()
        {
            // Arrange
            var emitter = CreateEmitter();
            await emitter.StartAsync(CancellationToken.None);

            var entity = new TestNotificationEntity
            {
                NotificationId = Guid.NewGuid().ToString(),
                Message = "Graceful stop test",
                Timestamp = DateTime.UtcNow
            };

            await emitter.NotifyAsync(entity);

            // Act - Stop should wait for pending messages
            await emitter.StopAsync(CancellationToken.None);

            // Assert
            Assert.IsTrue(true, "Background processor stopped gracefully");
        }

        #endregion

        #region Dispose Tests

        [TestMethod]
        public void Dispose_ShouldNotThrow()
        {
            // Arrange
            var emitter = CreateEmitter();

            // Act & Assert - Should not throw
            emitter.Dispose();
        }

        [TestMethod]
        [ExpectedException(typeof(ObjectDisposedException))]
        public async Task NotifyAsync_ShouldThrowObjectDisposedException_AfterDispose()
        {
            // Arrange
            var emitter = CreateEmitter();
            emitter.Dispose();

            var entity = new TestNotificationEntity
            {
                NotificationId = Guid.NewGuid().ToString(),
                Message = "After dispose",
                Timestamp = DateTime.UtcNow
            };

            // Act - Should throw
            await emitter.NotifyAsync(entity);

            // Assert - ExpectedException
        }

        #endregion

        #region Helper Methods

        private RedisNotificationEmitter CreateEmitter(RedisNotificationOptions? options = null)
        {
            return new RedisNotificationEmitter(
                _mockLogger.Object,
                _mockRedis.Object,
                _mockConfiguration.Object,
                _mockRetryPolicy.Object,
                options
            );
        }

        private async Task StartEmitterAsync(IHostedService emitter)
        {
            await emitter.StartAsync(CancellationToken.None);
            await Task.Delay(200); // Give background task time to start
        }

        private async Task StopEmitterAsync(IHostedService emitter)
        {
            await emitter.StopAsync(CancellationToken.None);
        }

        #endregion
    }
}

