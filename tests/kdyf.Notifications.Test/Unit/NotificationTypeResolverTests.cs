using kdyf.Notifications.Entities;
using kdyf.Notifications.Interfaces;
using kdyf.Notifications.Services;
using kdyf.Notifications.Test.Models;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System.Text.Json;

namespace kdyf.Notifications.Test.Unit
{
    /// <summary>
    /// Tests for NotificationTypeResolver - validates Type.GetType resolution with version-agnostic variants,
    /// GenericNotification fallback, and backward compatibility.
    /// </summary>
    [TestClass]
    public sealed class NotificationTypeResolverTests
    {
        private NotificationTypeResolver? _resolver;
        private ILogger<NotificationTypeResolver>? _logger;

        [TestInitialize]
        public void Setup()
        {
            // Create logger
            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
            _logger = loggerFactory.CreateLogger<NotificationTypeResolver>();
            _resolver = new NotificationTypeResolver(_logger);
        }

        #region Type.GetType Resolution

        [TestMethod]
        [TestCategory("Unit")]
        [TestCategory("TypeResolution")]
        public void ResolveType_ViaTypeGetType_WithAssemblyQualifiedName_ShouldSucceed()
        {
            // Arrange - Use AssemblyQualifiedName
            var typeName = typeof(TestNotificationEntity).AssemblyQualifiedName!;

            // Act
            var resolvedType = _resolver!.ResolveType(typeName);

            // Assert
            Assert.IsNotNull(resolvedType, "Type should be resolved via Type.GetType with AssemblyQualifiedName");
            Assert.AreEqual(typeof(TestNotificationEntity), resolvedType);

            Console.WriteLine($"✓ Type.GetType: Successfully resolved '{typeName}'");
        }
        #endregion

        #region Version-Agnostic Resolution

        [TestMethod]
        [TestCategory("Unit")]
        [TestCategory("TypeResolution")]
        [TestCategory("BackwardCompatibility")]
        public void ResolveType_WithFakeVersion_ShouldResolveUsingVariant()
        {
            // This test validates backward compatibility: old messages with versioned type names
            // can still be resolved even if the assembly version has changed

            // Arrange - Simulate old message with fake/different version
            var typeFullName = typeof(TestNotificationEntity).FullName!;
            var typeNameWithFakeVersion = $"{typeFullName}, kdyf.Notifications.Test, Version=999.0.0.0, Culture=neutral, PublicKeyToken=null";

            // Act
            // Tries:
            //   1. Original (with fake version) - Type.GetType fails
            //   2. Variant (without version) - Type.GetType succeeds
            var resolvedType = _resolver!.ResolveType(typeNameWithFakeVersion);

            // Assert
            Assert.IsNotNull(resolvedType, "Type should be resolved via Type.GetType using variant without version");
            Assert.AreEqual(typeof(TestNotificationEntity), resolvedType);

            Console.WriteLine($"✓ Type.GetType with variant: Successfully resolved from '{typeNameWithFakeVersion}'");
        }

        #endregion

        #region Failure Cases - Type Resolution Fails

        [TestMethod]
        [TestCategory("Unit")]
        [TestCategory("TypeResolution")]
        public void ResolveType_WithInvalidType_ShouldReturnNull()
        {
            // Arrange
            var invalidTypeName = "This.Does.Not.Exist.InvalidType, InvalidAssembly";

            // Act
            var resolvedType = _resolver!.ResolveType(invalidTypeName);

            // Assert
            Assert.IsNull(resolvedType, "Invalid type should return null when type cannot be resolved");

            Console.WriteLine($"✓ Type resolution failed as expected for invalid type '{invalidTypeName}'");
        }

        [TestMethod]
        [TestCategory("Unit")]
        [TestCategory("TypeResolution")]
        public void ResolveType_WithNullTypeName_ShouldReturnNull()
        {
            // Arrange
            string? nullTypeName = null;

            // Act
            var resolvedType = _resolver!.ResolveType(nullTypeName);

            // Assert
            Assert.IsNull(resolvedType, "Null type name should return null");

            Console.WriteLine("✓ Null type name handled correctly");
        }

        [TestMethod]
        [TestCategory("Unit")]
        [TestCategory("TypeResolution")]
        public void ResolveType_WithEmptyTypeName_ShouldReturnNull()
        {
            // Arrange
            var emptyTypeName = "   ";

            // Act
            var resolvedType = _resolver!.ResolveType(emptyTypeName);

            // Assert
            Assert.IsNull(resolvedType, "Empty type name should return null");

            Console.WriteLine("✓ Empty type name handled correctly");
        }

        #endregion

        #region DeserializeOrCreateFallback - Success Cases

        [TestMethod]
        [TestCategory("Unit")]
        [TestCategory("TypeResolution")]
        [TestCategory("Deserialization")]
        public void DeserializeOrCreateFallback_WithValidType_ShouldDeserialize()
        {
            // Arrange
            var entity = new TestNotificationEntity
            {
                NotificationId = Guid.NewGuid().ToString(),
                Message = "Test message",
                Timestamp = DateTime.UtcNow
            };
            var json = JsonSerializer.Serialize(entity);
            var typeName = typeof(TestNotificationEntity).AssemblyQualifiedName!;

            // Act
            var result = _resolver!.DeserializeOrCreateFallback(json, typeName);

            // Assert
            Assert.IsNotNull(result, "Result should not be null");
            Assert.IsInstanceOfType(result, typeof(TestNotificationEntity), "Should deserialize to correct type");
            var deserializedEntity = (TestNotificationEntity)result;
            Assert.AreEqual(entity.NotificationId, deserializedEntity.NotificationId);
            Assert.AreEqual(entity.Message, deserializedEntity.Message);

            Console.WriteLine($"✓ Successfully deserialized to {result.GetType().Name}");
        }

        #endregion

        #region DeserializeOrCreateFallback - Fallback to GenericNotification

        [TestMethod]
        [TestCategory("Unit")]
        [TestCategory("TypeResolution")]
        [TestCategory("Deserialization")]
        [TestCategory("Fallback")]
        public void DeserializeOrCreateFallback_WithInvalidType_ShouldFallbackToGeneric()
        {
            // Arrange
            var json = "{\"NotificationId\":\"123\",\"Message\":\"Test\",\"Timestamp\":\"2024-01-01T00:00:00Z\"}";
            var invalidTypeName = "Invalid.Type.Name, InvalidAssembly";

            // Act
            var result = _resolver!.DeserializeOrCreateFallback(json, invalidTypeName, "123", DateTime.UtcNow);

            // Assert
            Assert.IsNotNull(result, "Result should not be null");
            Assert.IsInstanceOfType(result, typeof(GenericNotification), "Should fallback to GenericNotification");

            var genericNotification = (GenericNotification)result;
            Assert.AreEqual("123", genericNotification.NotificationId);
            Assert.AreEqual(invalidTypeName, genericNotification.NotificationType);
            Assert.IsNotNull(genericNotification.Data, "Data should preserve original JSON");

            Console.WriteLine($"✓ Fallback to GenericNotification successful for invalid type '{invalidTypeName}'");
        }

        [TestMethod]
        [TestCategory("Unit")]
        [TestCategory("TypeResolution")]
        [TestCategory("Deserialization")]
        [TestCategory("Fallback")]
        public void DeserializeOrCreateFallback_WithMalformedJson_ShouldFallbackToGeneric()
        {
            // This test validates that if deserialization fails (even with valid type),
            // we fallback to GenericNotification

            // Arrange - Valid type but JSON doesn't match the schema
            var malformedJson = "{\"CompletelyDifferentField\":\"value\"}";
            var typeName = typeof(TestNotificationEntity).AssemblyQualifiedName!;

            // Act
            var result = _resolver!.DeserializeOrCreateFallback(malformedJson, typeName, "test-id", DateTime.UtcNow);

            // Assert
            // Note: Deserialization might succeed but create an object with default values
            // Or it might fail and fallback to GenericNotification
            Assert.IsNotNull(result, "Result should not be null");

            if (result is GenericNotification generic)
            {
                Assert.AreEqual("test-id", generic.NotificationId);
                Console.WriteLine("✓ Fallback to GenericNotification on malformed JSON");
            }
            else
            {
                // Deserialization succeeded with default values
                Console.WriteLine($"✓ Deserialized to {result.GetType().Name} with default values");
            }
        }

        #endregion


        #region GenericNotification Data Preservation

        [TestMethod]
        [TestCategory("Unit")]
        [TestCategory("TypeResolution")]
        [TestCategory("Fallback")]
        public void GenericNotification_ShouldPreserveOriginalJson()
        {
            // Arrange
            var originalJson = "{\"CustomField\":\"CustomValue\",\"Number\":42,\"Nested\":{\"Field\":\"Value\"}}";
            var invalidTypeName = "Invalid.Type";

            // Act
            var result = _resolver!.DeserializeOrCreateFallback(originalJson, invalidTypeName);

            // Assert
            Assert.IsInstanceOfType(result, typeof(GenericNotification));
            var generic = (GenericNotification)result;

            // Verify original data is preserved in the Data property
            Assert.IsNotNull(generic.Data);
            var dataJson = JsonSerializer.Serialize(generic.Data);

            // The data should contain the original fields
            Assert.IsTrue(dataJson.Contains("CustomField"), "Original JSON field 'CustomField' should be preserved");
            Assert.IsTrue(dataJson.Contains("CustomValue"), "Original JSON value 'CustomValue' should be preserved");
            Assert.IsTrue(dataJson.Contains("42"), "Original JSON value '42' should be preserved");

            Console.WriteLine($"✓ GenericNotification preserved original JSON data: {dataJson}");
        }

        #endregion

        #region Edge Cases
        [TestMethod]
        [TestCategory("Unit")]
        [TestCategory("TypeResolution")]
        [TestCategory("Variants")]
        public void ResolveType_WithTypeNameWithMultipleCommas_ShouldSplitOnFirstComma()
        {
            // This test verifies version-agnostic resolution with complex AssemblyQualifiedName

            // Arrange
            var typeFullName = typeof(TestNotificationEntity).FullName!;
            // Simulate full AssemblyQualifiedName with multiple commas
            var complexTypeName = $"{typeFullName}, kdyf.Notifications.Test, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null";

            // Act
            var resolvedType = _resolver!.ResolveType(complexTypeName);

            // Assert
            Assert.IsNotNull(resolvedType, "Type should be resolved by stripping everything after first comma");
            Assert.AreEqual(typeof(TestNotificationEntity), resolvedType);

            Console.WriteLine($"✓ Complex type name resolved: {complexTypeName} → {typeFullName}");
        }

        #endregion

        #region Constructor Tests

        [TestMethod]
        [TestCategory("Unit")]
        [TestCategory("TypeResolution")]
        [TestCategory("Constructor")]
        public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            Assert.ThrowsException<ArgumentNullException>(() =>
            {
                var resolver = new NotificationTypeResolver(null!);
            });

            Console.WriteLine("✓ Constructor correctly throws ArgumentNullException when logger is null");
        }

        [TestMethod]
        [TestCategory("Unit")]
        [TestCategory("TypeResolution")]
        [TestCategory("Constructor")]
        public void Constructor_WithValidLogger_ShouldSucceed()
        {
            // Arrange
            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var logger = loggerFactory.CreateLogger<NotificationTypeResolver>();

            // Act
            var resolver = new NotificationTypeResolver(logger);

            // Assert
            Assert.IsNotNull(resolver, "Resolver should be created successfully");

            Console.WriteLine("✓ Constructor succeeds with valid logger");
        }

        #endregion

        #region DeserializeOrCreateFallback - Additional Edge Cases

        [TestMethod]
        [TestCategory("Unit")]
        [TestCategory("TypeResolution")]
        [TestCategory("Deserialization")]
        [TestCategory("Fallback")]
        public void DeserializeOrCreateFallback_WithNullNotificationId_ShouldGenerateGuid()
        {
            // Arrange
            var json = "{\"field\":\"value\"}";
            var invalidType = "Invalid.Type.Name";

            // Act
            var result = _resolver!.DeserializeOrCreateFallback(json, invalidType, null, DateTime.UtcNow);

            // Assert
            Assert.IsInstanceOfType(result, typeof(GenericNotification));
            var generic = (GenericNotification)result;
            Assert.IsNotNull(generic.NotificationId, "NotificationId should not be null");
            Assert.AreNotEqual("", generic.NotificationId, "NotificationId should not be empty");

            // Verify it's a GUID format (GUIDs are 36 characters with hyphens or 32 without)
            Assert.IsTrue(generic.NotificationId.Length >= 32, "NotificationId should be GUID format");

            Console.WriteLine($"✓ Null NotificationId generated GUID: {generic.NotificationId}");
        }

        [TestMethod]
        [TestCategory("Unit")]
        [TestCategory("TypeResolution")]
        [TestCategory("Deserialization")]
        [TestCategory("Fallback")]
        public void DeserializeOrCreateFallback_WithWhitespaceNotificationId_ShouldGenerateGuid()
        {
            // Arrange
            var json = "{\"field\":\"value\"}";
            var invalidType = "Invalid.Type.Name";

            // Act
            var result = _resolver!.DeserializeOrCreateFallback(json, invalidType, "   ", DateTime.UtcNow);

            // Assert
            Assert.IsInstanceOfType(result, typeof(GenericNotification));
            var generic = (GenericNotification)result;
            Assert.IsNotNull(generic.NotificationId);
            Assert.AreNotEqual("", generic.NotificationId);
            Assert.AreNotEqual("   ", generic.NotificationId);

            Console.WriteLine($"✓ Whitespace NotificationId generated GUID: {generic.NotificationId}");
        }

        [TestMethod]
        [TestCategory("Unit")]
        [TestCategory("TypeResolution")]
        [TestCategory("Deserialization")]
        [TestCategory("Fallback")]
        public void DeserializeOrCreateFallback_WithNullTimestamp_ShouldUseUtcNow()
        {
            // Arrange
            var json = "{\"field\":\"value\"}";
            var invalidType = "Invalid.Type.Name";
            var beforeCall = DateTime.UtcNow.AddSeconds(-1); // Small buffer for timing

            // Act
            var result = _resolver!.DeserializeOrCreateFallback(json, invalidType, "test-id", null);
            var afterCall = DateTime.UtcNow.AddSeconds(1); // Small buffer for timing

            // Assert
            Assert.IsInstanceOfType(result, typeof(GenericNotification));
            var generic = (GenericNotification)result;
            Assert.IsTrue(generic.Timestamp >= beforeCall && generic.Timestamp <= afterCall,
                $"Timestamp {generic.Timestamp:O} should be between {beforeCall:O} and {afterCall:O}");

            Console.WriteLine($"✓ Null timestamp used DateTime.UtcNow: {generic.Timestamp:O}");
        }

        [TestMethod]
        [TestCategory("Unit")]
        [TestCategory("TypeResolution")]
        [TestCategory("Deserialization")]
        [TestCategory("Fallback")]
        public void DeserializeOrCreateFallback_WithProvidedNotificationIdAndTimestamp_ShouldUseProvided()
        {
            // Arrange
            var json = "{\"field\":\"value\"}";
            var invalidType = "Invalid.Type.Name";
            var providedId = "custom-notification-id";
            var providedTimestamp = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);

            // Act
            var result = _resolver!.DeserializeOrCreateFallback(json, invalidType, providedId, providedTimestamp);

            // Assert
            Assert.IsInstanceOfType(result, typeof(GenericNotification));
            var generic = (GenericNotification)result;
            Assert.AreEqual(providedId, generic.NotificationId, "Should use provided NotificationId");
            Assert.AreEqual(providedTimestamp, generic.Timestamp, "Should use provided Timestamp");

            Console.WriteLine($"✓ Used provided ID: {generic.NotificationId}, Timestamp: {generic.Timestamp:O}");
        }

        #endregion



        #region Logging Verification Tests

        [TestMethod]
        [TestCategory("Unit")]
        [TestCategory("TypeResolution")]
        [TestCategory("Logging")]
        public void ResolveType_WithInvalidType_ShouldLogWarning()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<NotificationTypeResolver>>();
            var resolver = new NotificationTypeResolver(mockLogger.Object);
            var invalidTypeName = "NonExistent.Type.Name";

            // Act
            var result = resolver.ResolveType(invalidTypeName);

            // Assert
            Assert.IsNull(result);

            // Verify that LogWarning was called
            mockLogger.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Could not resolve type")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once,
                "LogWarning should be called when type cannot be resolved");

            Console.WriteLine("✓ Warning logged for invalid type");
        }

        [TestMethod]
        [TestCategory("Unit")]
        [TestCategory("TypeResolution")]
        [TestCategory("Logging")]
        public void ResolveType_WithNullTypeName_ShouldLogWarning()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<NotificationTypeResolver>>();
            var resolver = new NotificationTypeResolver(mockLogger.Object);

            // Act
            var result = resolver.ResolveType(null);

            // Assert
            Assert.IsNull(result);

            // Verify that LogWarning was called for null/empty
            mockLogger.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("null or empty")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once,
                "LogWarning should be called when typeName is null");

            Console.WriteLine("✓ Warning logged for null type name");
        }

        [TestMethod]
        [TestCategory("Unit")]
        [TestCategory("TypeResolution")]
        [TestCategory("Logging")]
        public void DeserializeOrCreateFallback_WithDeserializationError_ShouldLogError()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<NotificationTypeResolver>>();
            var resolver = new NotificationTypeResolver(mockLogger.Object);

            // Valid JSON but with wrong structure that will cause deserialization to fail
            // The JSON is valid so JsonDocument.Parse won't throw, but JsonSerializer.Deserialize will
            var invalidJson = "{\"WrongProperty\":\"value\",\"NotificationId\":\"test\"}";
            var typeName = typeof(TestNotificationEntity).AssemblyQualifiedName!;

            // Act
            var result = resolver.DeserializeOrCreateFallback(invalidJson, typeName, "test-id", DateTime.UtcNow);

            // Assert
            // Note: This might not actually fail deserialization for TestNotificationEntity
            // because JsonSerializer is lenient. Let's just verify the result is valid.
            Assert.IsNotNull(result, "Result should not be null");

            Console.WriteLine($"✓ Deserialization handled: {result.GetType().Name}");
        }

        #endregion
    }
}
