using Moq;
using StackExchange.Redis;

namespace kdyf.Notifications.Test.Redis.Helpers
{
    /// <summary>
    /// Factory for creating Redis mocks for unit testing.
    /// Provides pre-configured mocks of IConnectionMultiplexer, IDatabase, and ISubscriber.
    /// </summary>
    public static class MockRedisFactory
    {
        /// <summary>
        /// Creates a complete Redis mock setup with ConnectionMultiplexer, Database, and Subscriber.
        /// </summary>
        /// <param name="isConnected">Whether the connection should report as connected (default: true).</param>
        /// <returns>A tuple containing mocked Redis components.</returns>
        public static (
            Mock<IConnectionMultiplexer> ConnectionMultiplexer,
            Mock<IDatabase> Database,
            Mock<ISubscriber> Subscriber
        ) CreateRedisMocks(bool isConnected = true)
        {
            var mockMultiplexer = new Mock<IConnectionMultiplexer>();
            var mockDatabase = new Mock<IDatabase>();
            var mockSubscriber = new Mock<ISubscriber>();

            // Setup connection state
            mockMultiplexer.Setup(x => x.IsConnected).Returns(isConnected);

            // Setup GetDatabase
            mockMultiplexer
                .Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
                .Returns(mockDatabase.Object);

            // Setup GetSubscriber
            mockMultiplexer
                .Setup(x => x.GetSubscriber(It.IsAny<object>()))
                .Returns(mockSubscriber.Object);

            // Setup common successful responses
            // StringSetAsync has signature: (key, value, expiry, keepTtl, when, flags)
            mockDatabase
                .Setup(x => x.StringSetAsync(
                    It.IsAny<RedisKey>(),
                    It.IsAny<RedisValue>(),
                    It.IsAny<TimeSpan?>(),
                    It.IsAny<bool>(),
                    It.IsAny<When>(),
                    It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);

            mockDatabase
                .Setup(x => x.StreamAddAsync(
                    It.IsAny<RedisKey>(),
                    It.IsAny<RedisValue>(),
                    It.IsAny<RedisValue>(),
                    It.IsAny<string?>(),
                    It.IsAny<int?>(),
                    It.IsAny<bool>(),
                    It.IsAny<CommandFlags>()))
                .ReturnsAsync(new RedisValue("1-0"));

            mockDatabase
                .Setup(x => x.KeyExistsAsync(
                    It.IsAny<RedisKey>(),
                    It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);

            mockDatabase
                .Setup(x => x.ExecuteAsync(
                    "PING",
                    It.IsAny<object[]>(),
                    It.IsAny<CommandFlags>()))
                .ReturnsAsync(RedisResult.Create(new RedisValue("PONG")));

            return (mockMultiplexer, mockDatabase, mockSubscriber);
        }

        /// <summary>
        /// Creates a Redis mock that simulates connection failures.
        /// </summary>
        /// <returns>A tuple containing mocked Redis components configured to fail.</returns>
        public static (
            Mock<IConnectionMultiplexer> ConnectionMultiplexer,
            Mock<IDatabase> Database,
            Mock<ISubscriber> Subscriber
        ) CreateFailingRedisMocks()
        {
            var (mockMultiplexer, mockDatabase, mockSubscriber) = CreateRedisMocks(isConnected: false);

            // Override successful setups with failures
            mockDatabase
                .Setup(x => x.StringSetAsync(
                    It.IsAny<RedisKey>(),
                    It.IsAny<RedisValue>(),
                    It.IsAny<TimeSpan?>(),
                    It.IsAny<bool>(),
                    It.IsAny<When>(),
                    It.IsAny<CommandFlags>()))
                .ThrowsAsync(new RedisConnectionException(
                    ConnectionFailureType.UnableToConnect,
                    "Simulated connection failure"));

            mockDatabase
                .Setup(x => x.StreamAddAsync(
                    It.IsAny<RedisKey>(),
                    It.IsAny<RedisValue>(),
                    It.IsAny<RedisValue>(),
                    It.IsAny<string?>(),
                    It.IsAny<int?>(),
                    It.IsAny<bool>(),
                    It.IsAny<CommandFlags>()))
                .ThrowsAsync(new RedisConnectionException(
                    ConnectionFailureType.UnableToConnect,
                    "Simulated connection failure"));

            mockDatabase
                .Setup(x => x.ExecuteAsync(
                    "PING",
                    It.IsAny<object[]>(),
                    It.IsAny<CommandFlags>()))
                .ThrowsAsync(new RedisConnectionException(
                    ConnectionFailureType.UnableToConnect,
                    "Simulated connection failure"));

            return (mockMultiplexer, mockDatabase, mockSubscriber);
        }

        /// <summary>
        /// Creates a Redis mock that simulates timeout failures.
        /// </summary>
        /// <returns>A tuple containing mocked Redis components configured to timeout.</returns>
        public static (
            Mock<IConnectionMultiplexer> ConnectionMultiplexer,
            Mock<IDatabase> Database,
            Mock<ISubscriber> Subscriber
        ) CreateTimeoutRedisMocks()
        {
            var (mockMultiplexer, mockDatabase, mockSubscriber) = CreateRedisMocks(isConnected: true);

            // Setup timeout exceptions
            mockDatabase
                .Setup(x => x.StringSetAsync(
                    It.IsAny<RedisKey>(),
                    It.IsAny<RedisValue>(),
                    It.IsAny<TimeSpan?>(),
                    It.IsAny<bool>(),
                    It.IsAny<When>(),
                    It.IsAny<CommandFlags>()))
                .ThrowsAsync(new RedisTimeoutException(
                    "Timeout awaiting response",
                    CommandStatus.WaitingToBeSent));

            mockDatabase
                .Setup(x => x.StreamAddAsync(
                    It.IsAny<RedisKey>(),
                    It.IsAny<RedisValue>(),
                    It.IsAny<RedisValue>(),
                    It.IsAny<string?>(),
                    It.IsAny<int?>(),
                    It.IsAny<bool>(),
                    It.IsAny<CommandFlags>()))
                .ThrowsAsync(new RedisTimeoutException(
                    "Timeout awaiting response",
                    CommandStatus.WaitingToBeSent));

            return (mockMultiplexer, mockDatabase, mockSubscriber);
        }
    }
}

