using kdyf.Notifications.Redis.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;

namespace kdyf.Notifications.Test.Redis.Unit
{
    /// <summary>
    /// Unit tests for RedisNotificationHealthCheck.
    /// Tests Redis connection monitoring and health reporting.
    /// </summary>
    [TestClass]
    public class RedisNotificationHealthCheckTests
    {
        private Mock<IConnectionMultiplexer> _mockRedis = null!;
        private Mock<IDatabase> _mockDatabase = null!;
        private Mock<ILogger<RedisNotificationHealthCheck>> _mockLogger = null!;
        private const string TestStreamName = "test-stream";

        [TestInitialize]
        public void Setup()
        {
            _mockRedis = new Mock<IConnectionMultiplexer>();
            _mockDatabase = new Mock<IDatabase>();
            _mockLogger = new Mock<ILogger<RedisNotificationHealthCheck>>();

            _mockRedis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
                .Returns(_mockDatabase.Object);
        }

        #region Constructor Tests

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_ShouldThrowArgumentNullException_WhenRedisIsNull()
        {
            // Act
            var healthCheck = new RedisNotificationHealthCheck(null!, _mockLogger.Object, TestStreamName);

            // Assert - ExpectedException
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_ShouldThrowArgumentNullException_WhenLoggerIsNull()
        {
            // Act
            var healthCheck = new RedisNotificationHealthCheck(_mockRedis.Object, null!, TestStreamName);

            // Assert - ExpectedException
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_ShouldThrowArgumentNullException_WhenStreamNameIsNull()
        {
            // Act
            var healthCheck = new RedisNotificationHealthCheck(_mockRedis.Object, _mockLogger.Object, null!);

            // Assert - ExpectedException
        }

        #endregion

        #region CheckHealthAsync - Healthy Tests

        [TestMethod]
        public async Task CheckHealthAsync_ShouldReturnHealthy_WhenRedisIsConnected()
        {
            // Arrange
            _mockRedis.Setup(r => r.IsConnected).Returns(true);
            _mockDatabase.Setup(d => d.PingAsync(It.IsAny<CommandFlags>()))
                .ReturnsAsync(TimeSpan.FromMilliseconds(50));
            _mockDatabase.Setup(d => d.KeyExistsAsync(TestStreamName, It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);

            var healthCheck = new RedisNotificationHealthCheck(_mockRedis.Object, _mockLogger.Object, TestStreamName);
            var context = new HealthCheckContext();

            // Act
            var result = await healthCheck.CheckHealthAsync(context);

            // Assert
            Assert.AreEqual(HealthStatus.Healthy, result.Status);
            Assert.IsTrue(result.Description?.Contains("operational") ?? false);
            Assert.IsTrue(result.Data.ContainsKey("StreamName"));
            Assert.IsTrue(result.Data.ContainsKey("StreamExists"));
            Assert.IsTrue(result.Data.ContainsKey("IsConnected"));
            Assert.IsTrue(result.Data.ContainsKey("PingLatency"));
        }

        [TestMethod]
        public async Task CheckHealthAsync_ShouldIncludeStreamName_InHealthData()
        {
            // Arrange
            _mockRedis.Setup(r => r.IsConnected).Returns(true);
            _mockDatabase.Setup(d => d.PingAsync(It.IsAny<CommandFlags>()))
                .ReturnsAsync(TimeSpan.FromMilliseconds(50));
            _mockDatabase.Setup(d => d.KeyExistsAsync(TestStreamName, It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);

            var healthCheck = new RedisNotificationHealthCheck(_mockRedis.Object, _mockLogger.Object, TestStreamName);
            var context = new HealthCheckContext();

            // Act
            var result = await healthCheck.CheckHealthAsync(context);

            // Assert
            Assert.AreEqual(TestStreamName, result.Data["StreamName"]);
        }

        #endregion

        #region CheckHealthAsync - Degraded Tests

        [TestMethod]
        public async Task CheckHealthAsync_ShouldReturnDegraded_WhenPingIsSlow()
        {
            // Arrange
            _mockRedis.Setup(r => r.IsConnected).Returns(true);
            _mockDatabase.Setup(d => d.PingAsync(It.IsAny<CommandFlags>()))
                .ReturnsAsync(TimeSpan.FromMilliseconds(1500)); // > 1000ms threshold
            _mockDatabase.Setup(d => d.KeyExistsAsync(TestStreamName, It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);

            var healthCheck = new RedisNotificationHealthCheck(_mockRedis.Object, _mockLogger.Object, TestStreamName);
            var context = new HealthCheckContext();

            // Act
            var result = await healthCheck.CheckHealthAsync(context);

            // Assert
            Assert.AreEqual(HealthStatus.Degraded, result.Status);
            Assert.IsTrue(result.Description?.Contains("slow") ?? false);
        }

        #endregion

        #region CheckHealthAsync - Unhealthy Tests

        [TestMethod]
        public async Task CheckHealthAsync_ShouldReturnUnhealthy_WhenRedisIsDisconnected()
        {
            // Arrange
            _mockRedis.Setup(r => r.IsConnected).Returns(false);

            var healthCheck = new RedisNotificationHealthCheck(_mockRedis.Object, _mockLogger.Object, TestStreamName);
            var context = new HealthCheckContext();

            // Act
            var result = await healthCheck.CheckHealthAsync(context);

            // Assert
            Assert.AreEqual(HealthStatus.Unhealthy, result.Status);
            Assert.IsTrue(result.Description?.Contains("not established") ?? false);
            Assert.AreEqual(false, result.Data["IsConnected"]);
        }

        [TestMethod]
        public async Task CheckHealthAsync_ShouldReturnUnhealthy_OnRedisConnectionException()
        {
            // Arrange
            _mockRedis.Setup(r => r.IsConnected).Returns(true);
            _mockDatabase.Setup(d => d.PingAsync(It.IsAny<CommandFlags>()))
                .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Test exception"));

            var healthCheck = new RedisNotificationHealthCheck(_mockRedis.Object, _mockLogger.Object, TestStreamName);
            var context = new HealthCheckContext();

            // Act
            var result = await healthCheck.CheckHealthAsync(context);

            // Assert
            Assert.AreEqual(HealthStatus.Unhealthy, result.Status);
            Assert.IsTrue(result.Description?.Contains("connection failed") ?? false);
            Assert.IsNotNull(result.Exception);
        }

        [TestMethod]
        public async Task CheckHealthAsync_ShouldReturnUnhealthy_OnGeneralException()
        {
            // Arrange
            _mockRedis.Setup(r => r.IsConnected).Returns(true);
            _mockDatabase.Setup(d => d.PingAsync(It.IsAny<CommandFlags>()))
                .ThrowsAsync(new InvalidOperationException("Test exception"));

            var healthCheck = new RedisNotificationHealthCheck(_mockRedis.Object, _mockLogger.Object, TestStreamName);
            var context = new HealthCheckContext();

            // Act
            var result = await healthCheck.CheckHealthAsync(context);

            // Assert
            Assert.AreEqual(HealthStatus.Unhealthy, result.Status);
            Assert.IsNotNull(result.Exception);
        }

        #endregion

        #region Stream Existence Tests

        [TestMethod]
        public async Task CheckHealthAsync_ShouldCheckStreamExists()
        {
            // Arrange
            _mockRedis.Setup(r => r.IsConnected).Returns(true);
            _mockDatabase.Setup(d => d.PingAsync(It.IsAny<CommandFlags>()))
                .ReturnsAsync(TimeSpan.FromMilliseconds(50));
            _mockDatabase.Setup(d => d.KeyExistsAsync(TestStreamName, It.IsAny<CommandFlags>()))
                .ReturnsAsync(false); // Stream does not exist

            var healthCheck = new RedisNotificationHealthCheck(_mockRedis.Object, _mockLogger.Object, TestStreamName);
            var context = new HealthCheckContext();

            // Act
            var result = await healthCheck.CheckHealthAsync(context);

            // Assert
            Assert.AreEqual(HealthStatus.Healthy, result.Status); // Still healthy even if stream doesn't exist
            Assert.AreEqual(false, result.Data["StreamExists"]);
        }

        #endregion

        #region Cancellation Tests

        [TestMethod]
        public async Task CheckHealthAsync_ShouldRespectCancellationToken()
        {
            // Arrange
            _mockRedis.Setup(r => r.IsConnected).Returns(true);
            _mockDatabase.Setup(d => d.PingAsync(It.IsAny<CommandFlags>()))
                .ReturnsAsync(TimeSpan.FromMilliseconds(50));

            var healthCheck = new RedisNotificationHealthCheck(_mockRedis.Object, _mockLogger.Object, TestStreamName);
            var context = new HealthCheckContext();
            var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act
            var result = await healthCheck.CheckHealthAsync(context, cts.Token);

            // Assert
            Assert.IsNotNull(result);
        }

        #endregion

        #region Data Validation Tests

        [TestMethod]
        public async Task CheckHealthAsync_ShouldIncludePingLatency_InData()
        {
            // Arrange
            var expectedLatency = TimeSpan.FromMilliseconds(75);
            _mockRedis.Setup(r => r.IsConnected).Returns(true);
            _mockDatabase.Setup(d => d.PingAsync(It.IsAny<CommandFlags>()))
                .ReturnsAsync(expectedLatency);
            _mockDatabase.Setup(d => d.KeyExistsAsync(TestStreamName, It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);

            var healthCheck = new RedisNotificationHealthCheck(_mockRedis.Object, _mockLogger.Object, TestStreamName);
            var context = new HealthCheckContext();

            // Act
            var result = await healthCheck.CheckHealthAsync(context);

            // Assert
            Assert.IsTrue(result.Data.ContainsKey("PingLatency"));
            Assert.AreEqual(expectedLatency.TotalMilliseconds, result.Data["PingLatency"]);
        }

        [TestMethod]
        public async Task CheckHealthAsync_ShouldIncludeConnectionStatus_InData()
        {
            // Arrange
            _mockRedis.Setup(r => r.IsConnected).Returns(true);
            _mockDatabase.Setup(d => d.PingAsync(It.IsAny<CommandFlags>()))
                .ReturnsAsync(TimeSpan.FromMilliseconds(50));
            _mockDatabase.Setup(d => d.KeyExistsAsync(TestStreamName, It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);

            var healthCheck = new RedisNotificationHealthCheck(_mockRedis.Object, _mockLogger.Object, TestStreamName);
            var context = new HealthCheckContext();

            // Act
            var result = await healthCheck.CheckHealthAsync(context);

            // Assert
            Assert.IsTrue(result.Data.ContainsKey("IsConnected"));
            Assert.AreEqual(true, result.Data["IsConnected"]);
        }

        #endregion
    }
}
