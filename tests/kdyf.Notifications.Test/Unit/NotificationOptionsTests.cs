using kdyf.Notifications.Configuration;

namespace kdyf.Notifications.Test.Unit
{
    /// <summary>
    /// Unit tests for NotificationOptions configuration class.
    /// </summary>
    [TestClass]
    public class NotificationOptionsTests
    {
        #region Constructor and Default Values

        [TestMethod]
        public void Constructor_ShouldInitializeWithDefaultValues()
        {
            // Act
            var options = new NotificationOptions();

            // Assert
            Assert.AreEqual(TimeSpan.FromMinutes(10), options.DeduplicationTtl);
            Assert.AreEqual(10_000, options.MaxDeduplicationCacheSize);
            Assert.AreEqual(0.25, options.CacheCompactionPercentage);
            Assert.AreEqual(TimeSpan.FromMinutes(1), options.CacheScanInterval);
        }

        #endregion

        #region Validation Tests

        [TestMethod]
        public void Validate_ShouldNotThrow_WhenAllValuesAreValid()
        {
            // Arrange
            var options = new NotificationOptions
            {
                DeduplicationTtl = TimeSpan.FromMinutes(5),
                MaxDeduplicationCacheSize = 5000,
                CacheCompactionPercentage = 0.3,
                CacheScanInterval = TimeSpan.FromSeconds(30)
            };

            // Act & Assert - Should not throw
            options.Validate();
        }

        [TestMethod]
        public void Validate_ShouldThrow_WhenDeduplicationTtlIsZero()
        {
            // Arrange
            var options = new NotificationOptions
            {
                DeduplicationTtl = TimeSpan.Zero
            };

            // Act & Assert
            var ex = Assert.ThrowsException<ArgumentException>(() => options.Validate());
            Assert.IsTrue(ex.Message.Contains("DeduplicationTtl"));
        }

        [TestMethod]
        public void Validate_ShouldThrow_WhenDeduplicationTtlIsNegative()
        {
            // Arrange
            var options = new NotificationOptions
            {
                DeduplicationTtl = TimeSpan.FromMinutes(-1)
            };

            // Act & Assert
            var ex = Assert.ThrowsException<ArgumentException>(() => options.Validate());
            Assert.IsTrue(ex.Message.Contains("DeduplicationTtl"));
        }

        [TestMethod]
        public void Validate_ShouldThrow_WhenMaxCacheSizeIsZero()
        {
            // Arrange
            var options = new NotificationOptions
            {
                MaxDeduplicationCacheSize = 0
            };

            // Act & Assert
            var ex = Assert.ThrowsException<ArgumentException>(() => options.Validate());
            Assert.IsTrue(ex.Message.Contains("MaxDeduplicationCacheSize"));
        }

        [TestMethod]
        public void Validate_ShouldThrow_WhenMaxCacheSizeIsNegative()
        {
            // Arrange
            var options = new NotificationOptions
            {
                MaxDeduplicationCacheSize = -100
            };

            // Act & Assert
            var ex = Assert.ThrowsException<ArgumentException>(() => options.Validate());
            Assert.IsTrue(ex.Message.Contains("MaxDeduplicationCacheSize"));
        }

        [TestMethod]
        public void Validate_ShouldThrow_WhenCompactionPercentageIsZero()
        {
            // Arrange
            var options = new NotificationOptions
            {
                CacheCompactionPercentage = 0
            };

            // Act & Assert
            var ex = Assert.ThrowsException<ArgumentException>(() => options.Validate());
            Assert.IsTrue(ex.Message.Contains("CacheCompactionPercentage"));
        }

        [TestMethod]
        public void Validate_ShouldThrow_WhenCompactionPercentageIsOne()
        {
            // Arrange
            var options = new NotificationOptions
            {
                CacheCompactionPercentage = 1.0
            };

            // Act & Assert
            var ex = Assert.ThrowsException<ArgumentException>(() => options.Validate());
            Assert.IsTrue(ex.Message.Contains("CacheCompactionPercentage"));
        }

        [TestMethod]
        public void Validate_ShouldThrow_WhenCompactionPercentageIsNegative()
        {
            // Arrange
            var options = new NotificationOptions
            {
                CacheCompactionPercentage = -0.1
            };

            // Act & Assert
            var ex = Assert.ThrowsException<ArgumentException>(() => options.Validate());
            Assert.IsTrue(ex.Message.Contains("CacheCompactionPercentage"));
        }

        [TestMethod]
        public void Validate_ShouldThrow_WhenCacheScanIntervalIsZero()
        {
            // Arrange
            var options = new NotificationOptions
            {
                CacheScanInterval = TimeSpan.Zero
            };

            // Act & Assert
            var ex = Assert.ThrowsException<ArgumentException>(() => options.Validate());
            Assert.IsTrue(ex.Message.Contains("CacheScanInterval"));
        }

        #endregion

        #region ToMemoryCacheOptions Tests

        [TestMethod]
        public void ToMemoryCacheOptions_ShouldReturnConfiguredOptions()
        {
            // Arrange
            var options = new NotificationOptions
            {
                MaxDeduplicationCacheSize = 5000,
                CacheCompactionPercentage = 0.3,
                CacheScanInterval = TimeSpan.FromSeconds(30)
            };

            // Act
            var memoryCacheOptions = options.ToMemoryCacheOptions();

            // Assert
            Assert.IsNotNull(memoryCacheOptions);
            Assert.AreEqual(5000, memoryCacheOptions.SizeLimit);
            Assert.AreEqual(0.3, memoryCacheOptions.CompactionPercentage);
            Assert.AreEqual(TimeSpan.FromSeconds(30), memoryCacheOptions.ExpirationScanFrequency);
        }

        [TestMethod]
        public void ToMemoryCacheOptions_ShouldUseDefaultValues()
        {
            // Arrange
            var options = new NotificationOptions();

            // Act
            var memoryCacheOptions = options.ToMemoryCacheOptions();

            // Assert
            Assert.IsNotNull(memoryCacheOptions);
            Assert.AreEqual(10_000, memoryCacheOptions.SizeLimit);
            Assert.AreEqual(0.25, memoryCacheOptions.CompactionPercentage);
            Assert.AreEqual(TimeSpan.FromMinutes(1), memoryCacheOptions.ExpirationScanFrequency);
        }

        #endregion

        #region Custom Configuration Tests

        [TestMethod]
        public void Options_ShouldSupportCustomCacheSize()
        {
            // Arrange & Act
            var options = new NotificationOptions
            {
                MaxDeduplicationCacheSize = 50_000
            };

            // Assert
            Assert.AreEqual(50_000, options.MaxDeduplicationCacheSize);
            options.Validate(); // Should not throw
        }

        [TestMethod]
        public void Options_ShouldSupportCustomTtl()
        {
            // Arrange & Act
            var options = new NotificationOptions
            {
                DeduplicationTtl = TimeSpan.FromMinutes(30)
            };

            // Assert
            Assert.AreEqual(TimeSpan.FromMinutes(30), options.DeduplicationTtl);
            options.Validate(); // Should not throw
        }

        #endregion
    }
}
