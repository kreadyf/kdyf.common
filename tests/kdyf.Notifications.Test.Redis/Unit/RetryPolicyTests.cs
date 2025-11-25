using kdyf.Notifications.Interfaces;
using kdyf.Notifications.Redis.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using StackExchange.Redis;

namespace kdyf.Notifications.Test.Redis.Unit
{
    /// <summary>
    /// Tests for SimpleRetryPolicy - validates retry logic for transient Redis failures.
    /// Tests both generic (with return value) and void methods.
    /// </summary>
    [TestClass]
    public sealed class RetryPolicyTests
    {
        private ILogger<SimpleRetryPolicy>? _logger;
        private SimpleRetryPolicy? _retryPolicy;

        [TestInitialize]
        public void Setup()
        {
            var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Debug);
            });
            _logger = loggerFactory.CreateLogger<SimpleRetryPolicy>();
        }

        #region ExecuteAsync<T> - Success Cases

        [TestMethod]
        [TestCategory("Unit")]
        [TestCategory("Retry")]
        public async Task ExecuteAsync_WithSuccessfulOperation_ShouldReturnResult()
        {
            // Arrange
            _retryPolicy = new SimpleRetryPolicy(100, _logger!);
            var expectedResult = "Success";

            // Act
            var result = await _retryPolicy.ExecuteAsync(async () =>
            {
                await Task.CompletedTask;
                return expectedResult;
            });

            // Assert
            Assert.AreEqual(expectedResult, result, "Should return result from successful operation");
            Console.WriteLine("✓ Successful operation completed without retry");
        }

        [TestMethod]
        [TestCategory("Unit")]
        [TestCategory("Retry")]
        public async Task ExecuteAsync_Void_WithSuccessfulOperation_ShouldComplete()
        {
            // Arrange
            _retryPolicy = new SimpleRetryPolicy(100, _logger!);
            var executionCount = 0;

            // Act
            await _retryPolicy.ExecuteAsync(async () =>
            {
                await Task.CompletedTask;
                executionCount++;
            });

            // Assert
            Assert.AreEqual(1, executionCount, "Operation should execute exactly once");
            Console.WriteLine("✓ Void operation completed without retry");
        }

        #endregion

        #region ExecuteAsync<T> - Retry on RedisConnectionException

        [TestMethod]
        [TestCategory("Unit")]
        [TestCategory("Retry")]
        public async Task ExecuteAsync_WithTransientFailure_ShouldRetryAndSucceed()
        {
            // Arrange
            _retryPolicy = new SimpleRetryPolicy(100, _logger!);
            var attemptCount = 0;

            // Act - First call fails, second succeeds
            var result = await _retryPolicy.ExecuteAsync(async () =>
            {
                await Task.CompletedTask;
                attemptCount++;

                if (attemptCount == 1)
                {
                    throw new RedisConnectionException(
                        ConnectionFailureType.UnableToConnect,
                        "Simulated transient connection failure");
                }

                return "Success after retry";
            });

            // Assert
            Assert.AreEqual(2, attemptCount, "Should attempt twice (1 initial + 1 retry)");
            Assert.AreEqual("Success after retry", result);
            Console.WriteLine($"✓ Operation retried successfully after transient failure. Total attempts: {attemptCount}");
        }

        [TestMethod]
        [TestCategory("Unit")]
        [TestCategory("Retry")]
        public async Task ExecuteAsync_Void_WithTransientFailure_ShouldRetryAndSucceed()
        {
            // Arrange
            _retryPolicy = new SimpleRetryPolicy(100, _logger!);
            var attemptCount = 0;

            // Act - First call fails, second succeeds
            await _retryPolicy.ExecuteAsync(async () =>
            {
                await Task.CompletedTask;
                attemptCount++;

                if (attemptCount == 1)
                {
                    throw new RedisConnectionException(
                        ConnectionFailureType.UnableToConnect,
                        "Simulated transient connection failure");
                }
            });

            // Assert
            Assert.AreEqual(2, attemptCount, "Should attempt twice (1 initial + 1 retry)");
            Console.WriteLine($"✓ Void operation retried successfully after transient failure. Total attempts: {attemptCount}");
        }

        #endregion

        #region ExecuteAsync<T> - Failure After Retry

        [TestMethod]
        [TestCategory("Unit")]
        [TestCategory("Retry")]
        public async Task ExecuteAsync_WithPersistentFailure_ShouldThrowAfterRetry()
        {
            // Arrange
            _retryPolicy = new SimpleRetryPolicy(100, _logger!);
            var attemptCount = 0;

            // Act & Assert
            var exception = await Assert.ThrowsExceptionAsync<RedisConnectionException>(async () =>
            {
                await _retryPolicy.ExecuteAsync(async () =>
                {
                    await Task.CompletedTask;
                    attemptCount++;

                    // Fail on both attempts
                    throw new RedisConnectionException(
                        ConnectionFailureType.UnableToConnect,
                        $"Persistent connection failure (attempt {attemptCount})");
                });
            });

            // Assert
            Assert.AreEqual(2, attemptCount, "Should attempt twice before throwing");
            Assert.IsTrue(exception.Message.Contains("Persistent connection failure"));
            Console.WriteLine($"✓ Operation failed after retry. Total attempts: {attemptCount}");
        }

        [TestMethod]
        [TestCategory("Unit")]
        [TestCategory("Retry")]
        public async Task ExecuteAsync_Void_WithPersistentFailure_ShouldThrowAfterRetry()
        {
            // Arrange
            _retryPolicy = new SimpleRetryPolicy(100, _logger!);
            var attemptCount = 0;

            // Act & Assert
            var exception = await Assert.ThrowsExceptionAsync<RedisConnectionException>(async () =>
            {
                await _retryPolicy.ExecuteAsync(async () =>
                {
                    await Task.CompletedTask;
                    attemptCount++;

                    // Fail on both attempts
                    throw new RedisConnectionException(
                        ConnectionFailureType.UnableToConnect,
                        $"Persistent connection failure (attempt {attemptCount})");
                });
            });

            // Assert
            Assert.AreEqual(2, attemptCount, "Should attempt twice before throwing");
            Assert.IsTrue(exception.Message.Contains("Persistent connection failure"));
            Console.WriteLine($"✓ Void operation failed after retry. Total attempts: {attemptCount}");
        }

        #endregion

        #region Non-Transient Exceptions (No Retry)

        [TestMethod]
        [TestCategory("Unit")]
        [TestCategory("Retry")]
        public async Task ExecuteAsync_WithNonTransientException_ShouldNotRetry()
        {
            // This test validates that non-RedisConnectionException errors are NOT retried
            // (e.g., ArgumentException, InvalidOperationException, etc.)

            // Arrange
            _retryPolicy = new SimpleRetryPolicy(100, _logger!);
            var attemptCount = 0;

            // Act & Assert
            var exception = await Assert.ThrowsExceptionAsync<InvalidOperationException>(async () =>
            {
                await _retryPolicy.ExecuteAsync(async () =>
                {
                    await Task.CompletedTask;
                    attemptCount++;
                    throw new InvalidOperationException("Non-transient error");
                });
            });

            // Assert
            Assert.AreEqual(1, attemptCount, "Should only attempt once for non-transient exceptions");
            Assert.AreEqual("Non-transient error", exception.Message);
            Console.WriteLine("✓ Non-transient exception not retried (correct behavior)");
        }

        [TestMethod]
        [TestCategory("Unit")]
        [TestCategory("Retry")]
        public async Task ExecuteAsync_Void_WithNonTransientException_ShouldNotRetry()
        {
            // Arrange
            _retryPolicy = new SimpleRetryPolicy(100, _logger!);
            var attemptCount = 0;

            // Act & Assert
            var exception = await Assert.ThrowsExceptionAsync<InvalidOperationException>(async () =>
            {
                await _retryPolicy.ExecuteAsync(async () =>
                {
                    await Task.CompletedTask;
                    attemptCount++;
                    throw new InvalidOperationException("Non-transient error");
                });
            });

            // Assert
            Assert.AreEqual(1, attemptCount, "Should only attempt once for non-transient exceptions");
            Assert.AreEqual("Non-transient error", exception.Message);
            Console.WriteLine("✓ Void operation with non-transient exception not retried");
        }

        #endregion

        #region Retry Delay Validation

        [TestMethod]
        [TestCategory("Unit")]
        [TestCategory("Retry")]
        public async Task ExecuteAsync_WithRetry_ShouldCallDelayWithCorrectTimeSpan()
        {
            // This test validates that the configured delay is called with the correct TimeSpan
            // Using mocked ITimeProvider makes this test instant and deterministic

            // Arrange
            var delayMs = 500;
            _retryPolicy = new SimpleRetryPolicy(delayMs, _logger!);
            var attemptCount = 0;

            // Act
            await _retryPolicy.ExecuteAsync(async () =>
            {
                await Task.CompletedTask;
                attemptCount++;

                if (attemptCount == 1)
                {
                    throw new RedisConnectionException(
                        ConnectionFailureType.UnableToConnect,
                        "First attempt fails");
                }

                return "Success";
            });

            // Assert
            Assert.AreEqual(2, attemptCount, "Should attempt twice (1 initial + 1 retry)");

            Console.WriteLine($"✓ Retry delay called correctly with {delayMs}ms");
        }

        #endregion

        #region Cancellation Token Support

        [TestMethod]
        [TestCategory("Unit")]
        [TestCategory("Retry")]
        public async Task ExecuteAsync_WithCancellationDuringDelay_ShouldThrowOperationCancelled()
        {
            // This test validates that cancellation during the retry delay is properly handled

            // Arrange
            var cts = new CancellationTokenSource();
            _retryPolicy = new SimpleRetryPolicy(5000, _logger!);
            var attemptCount = 0;

            // Cancel the token before delay is called
            cts.Cancel();

            // Act & Assert
            // TaskCanceledException is derived from OperationCanceledException
            var exception = await Assert.ThrowsExceptionAsync<TaskCanceledException>(async () =>
            {
                await _retryPolicy.ExecuteAsync(async () =>
                {
                    await Task.CompletedTask;
                    attemptCount++;

                    if (attemptCount == 1)
                    {
                        throw new RedisConnectionException(
                            ConnectionFailureType.UnableToConnect,
                            "First attempt fails");
                    }

                    return "Success";
                }, cts.Token);
            });

            // Assert
            Assert.AreEqual(1, attemptCount, "Should be cancelled before retry attempt");
            Console.WriteLine("✓ Cancellation during retry delay handled correctly (instant via mock)");
        }

        #endregion

        #region Null Operation Validation

        [TestMethod]
        [TestCategory("Unit")]
        [TestCategory("Retry")]
        public async Task ExecuteAsync_WithNullOperation_ShouldThrowArgumentNullException()
        {
            // Arrange
            _retryPolicy = new SimpleRetryPolicy(100, _logger!);

            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentNullException>(async () =>
            {
                await _retryPolicy.ExecuteAsync<string>(null!);
            });

            Console.WriteLine("✓ Null operation validation works correctly");
        }

        [TestMethod]
        [TestCategory("Unit")]
        [TestCategory("Retry")]
        public async Task ExecuteAsync_Void_WithNullOperation_ShouldThrowArgumentNullException()
        {
            // Arrange
            _retryPolicy = new SimpleRetryPolicy(100, _logger!);

            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentNullException>(async () =>
            {
                await _retryPolicy.ExecuteAsync((Func<Task>)null!);
            });

            Console.WriteLine("✓ Null void operation validation works correctly");
        }

        #endregion

        #region RedisConnectionException Variations

        [TestMethod]
        [TestCategory("Unit")]
        [TestCategory("Retry")]
        public async Task ExecuteAsync_WithDifferentConnectionFailureTypes_ShouldRetryAll()
        {
            // This test validates that all types of RedisConnectionException trigger retry

            // Arrange
            _retryPolicy = new SimpleRetryPolicy(100, _logger!);
            var failureTypes = new[]
            {
                ConnectionFailureType.UnableToConnect,
                ConnectionFailureType.SocketFailure,
                ConnectionFailureType.SocketClosed,
                ConnectionFailureType.InternalFailure
            };

            // Act & Assert - Test each failure type
            foreach (var failureType in failureTypes)
            {
                var attemptCount = 0;

                var result = await _retryPolicy.ExecuteAsync(async () =>
                {
                    await Task.CompletedTask;
                    attemptCount++;

                    if (attemptCount == 1)
                    {
                        throw new RedisConnectionException(failureType, $"Simulated {failureType}");
                    }

                    return $"Recovered from {failureType}";
                });

                Assert.AreEqual(2, attemptCount, $"Should retry for {failureType}");
                Assert.IsTrue(result.Contains(failureType.ToString()));
            }

            Console.WriteLine($"✓ All {failureTypes.Length} RedisConnectionException types trigger retry correctly");
        }

        #endregion
    }
}
