using kdyf.Notifications.Entities;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace kdyf.Notifications.Test.Unit
{
    /// <summary>
    /// Unit tests for the <see cref="GenericNotification"/> class.
    /// </summary>
    [TestClass]
    public sealed class GenericNotificationTests
    {
        #region Constructor Tests

        /// <summary>
        /// Tests that default constructor initializes all properties.
        /// </summary>
        [TestMethod]
        public void Constructor_Default_ShouldInitializeProperties()
        {
            // Act
            var notification = new GenericNotification();

            // Assert
            Assert.AreEqual(string.Empty, notification.NotificationId);
            Assert.AreEqual(default(DateTime), notification.Timestamp);
            Assert.AreEqual(string.Empty, notification.NotificationType);
            Assert.AreEqual(string.Empty, notification.Message);
            Assert.AreEqual(LogLevel.Information, notification.Level);
            Assert.IsNotNull(notification.Tags);
            Assert.AreEqual(0, notification.Tags.Count);
        }

        /// <summary>
        /// Tests that parameterized constructor sets all properties correctly.
        /// </summary>
        [TestMethod]
        public void Constructor_Parameterized_ShouldSetAllProperties()
        {
            // Arrange
            var id = Guid.NewGuid().ToString();
            var timestamp = DateTime.UtcNow;
            var typeName = "MyApp.TestNotification";
            var jsonData = JsonDocument.Parse("{\"value\":123}");
            var tags = new HashSet<string> { "test", "important" };

            // Act
            var notification = new GenericNotification(id, timestamp, typeName, jsonData.RootElement, tags);

            // Assert
            Assert.AreEqual(id, notification.NotificationId);
            Assert.AreEqual(timestamp, notification.Timestamp);
            Assert.AreEqual(typeName, notification.NotificationType);
            Assert.AreEqual(tags, notification.Tags);
            Assert.IsTrue(notification.Data.GetProperty("value").GetInt32() == 123);
        }

        /// <summary>
        /// Tests that parameterized constructor handles null tags.
        /// </summary>
        [TestMethod]
        public void Constructor_Parameterized_ShouldHandleNullTags()
        {
            // Arrange
            var id = Guid.NewGuid().ToString();
            var timestamp = DateTime.UtcNow;
            var jsonData = JsonDocument.Parse("{}");

            // Act
            var notification = new GenericNotification(id, timestamp, "TestType", jsonData.RootElement, null);

            // Assert
            Assert.IsNotNull(notification.Tags);
            Assert.AreEqual(0, notification.Tags.Count);
        }

        #endregion

        #region IsGenericFallback Tests

        /// <summary>
        /// Tests that IsGenericFallback always returns true.
        /// </summary>
        [TestMethod]
        public void IsGenericFallback_ShouldAlwaysReturnTrue()
        {
            // Arrange
            var notification = new GenericNotification();

            // Act & Assert
            Assert.IsTrue(notification.IsGenericFallback);
        }

        #endregion

        #region GetRawJson Tests

        /// <summary>
        /// Tests that GetRawJson returns the correct JSON string.
        /// </summary>
        [TestMethod]
        public void GetRawJson_ShouldReturnJsonString()
        {
            // Arrange
            var json = "{\"name\":\"test\",\"value\":42}";
            var jsonData = JsonDocument.Parse(json);
            var notification = new GenericNotification
            {
                Data = jsonData.RootElement
            };

            // Act
            var result = notification.GetRawJson();

            // Assert
            Assert.IsNotNull(result);
            // The JSON might have different whitespace, so parse and compare
            var parsed = JsonDocument.Parse(result);
            Assert.AreEqual("test", parsed.RootElement.GetProperty("name").GetString());
            Assert.AreEqual(42, parsed.RootElement.GetProperty("value").GetInt32());
        }

        /// <summary>
        /// Tests that GetRawJson handles complex nested JSON.
        /// </summary>
        [TestMethod]
        public void GetRawJson_ShouldHandleComplexJson()
        {
            // Arrange
            var json = @"{
                ""user"": {
                    ""id"": 123,
                    ""name"": ""John Doe"",
                    ""roles"": [""admin"", ""user""]
                },
                ""metadata"": {
                    ""created"": ""2024-01-01T00:00:00Z""
                }
            }";
            var jsonData = JsonDocument.Parse(json);
            var notification = new GenericNotification
            {
                Data = jsonData.RootElement
            };

            // Act
            var result = notification.GetRawJson();

            // Assert
            Assert.IsNotNull(result);
            var parsed = JsonDocument.Parse(result);
            Assert.AreEqual(123, parsed.RootElement.GetProperty("user").GetProperty("id").GetInt32());
            Assert.AreEqual("John Doe", parsed.RootElement.GetProperty("user").GetProperty("name").GetString());
        }

        #endregion

        #region TryDeserialize Tests

        /// <summary>
        /// Tests that TryDeserialize successfully deserializes to target type.
        /// </summary>
        [TestMethod]
        public void TryDeserialize_ShouldDeserializeCorrectly()
        {
            // Arrange
            var testData = new { Name = "Test", Value = 123, IsActive = true };
            var json = JsonSerializer.Serialize(testData);
            var jsonData = JsonDocument.Parse(json);
            var notification = new GenericNotification
            {
                Data = jsonData.RootElement
            };

            // Act
            var result = notification.TryDeserialize<TestData>();

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual("Test", result!.Name);
            Assert.AreEqual(123, result.Value);
            Assert.IsTrue(result.IsActive);
        }

        /// <summary>
        /// Tests that TryDeserialize returns default when deserialization fails.
        /// </summary>
        [TestMethod]
        public void TryDeserialize_ShouldReturnDefault_OnFailure()
        {
            // Arrange: Use a primitive JSON value that cannot be deserialized to an object
            var json = "\"just a string\"";  // A string, not an object
            var jsonData = JsonDocument.Parse(json);
            var notification = new GenericNotification
            {
                Data = jsonData.RootElement
            };

            // Act
            var result = notification.TryDeserialize<ComplexTestData>();

            // Assert: Should return null/default because a string cannot be deserialized to a complex object
            Assert.IsNull(result);
        }

        /// <summary>
        /// Tests that TryDeserialize handles custom JsonSerializerOptions.
        /// </summary>
        [TestMethod]
        public void TryDeserialize_ShouldRespectCustomOptions()
        {
            // Arrange
            var json = "{\"custom_name\":\"Test\"}";
            var jsonData = JsonDocument.Parse(json);
            var notification = new GenericNotification
            {
                Data = jsonData.RootElement
            };

            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
            };

            // Act
            var result = notification.TryDeserialize<CustomNameData>(options);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual("Test", result!.CustomName);
        }

        #endregion

        #region ToString Tests

        /// <summary>
        /// Tests that ToString returns meaningful information.
        /// </summary>
        [TestMethod]
        public void ToString_ShouldContainKeyInfo()
        {
            // Arrange
            var id = Guid.NewGuid().ToString();
            var timestamp = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            var typeName = "MyApp.TestNotification";
            var jsonData = JsonDocument.Parse("{}");

            var notification = new GenericNotification(id, timestamp, typeName, jsonData.RootElement);

            // Act
            var result = notification.ToString();

            // Assert
            Assert.IsTrue(result.Contains(id.ToString()));
            Assert.IsTrue(result.Contains(typeName));
            Assert.IsTrue(result.Contains("2024-01-01"));
        }

        #endregion

        #region Integration Tests

        /// <summary>
        /// Tests a complete round-trip: serialize unknown type, deserialize to GenericNotification, extract data.
        /// </summary>
        [TestMethod]
        public void Integration_ShouldPreserveUnknownTypeData()
        {
            // Arrange: Simulate a notification from unknown type
            var originalData = new
            {
                OrderId = "ORD-12345",
                CustomerId = 999,
                Items = new[] { "Item1", "Item2" },
                Total = 123.45m
            };

            var json = JsonSerializer.Serialize(originalData);
            var jsonData = JsonDocument.Parse(json);

            // Act: Create GenericNotification as fallback
            var notification = new GenericNotification
            {
                NotificationId = Guid.NewGuid().ToString(),
                Timestamp = DateTime.UtcNow,
                NotificationType = "UnknownOrderNotification",
                Data = jsonData.RootElement
            };

            // Assert: All data should be preserved
            var rawJson = notification.GetRawJson();
            var restored = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(rawJson);

            Assert.IsNotNull(restored);
            Assert.AreEqual("ORD-12345", restored["OrderId"].GetString());
            Assert.AreEqual(999, restored["CustomerId"].GetInt32());
            Assert.AreEqual(2, restored["Items"].GetArrayLength());
        }

        #endregion

        #region Test Helper Classes

        private class TestData
        {
            public string Name { get; set; } = string.Empty;
            public int Value { get; set; }
            public bool IsActive { get; set; }
        }

        private class ComplexTestData
        {
            public string RequiredField { get; set; } = string.Empty;
            public NestedData Nested { get; set; } = new();
        }

        private class NestedData
        {
            public int Id { get; set; }
        }

        private class CustomNameData
        {
            public string CustomName { get; set; } = string.Empty;
        }

        #endregion
    }
}
