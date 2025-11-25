using kdyf.Notifications.Redis.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using StackExchange.Redis;

namespace kdyf.Notifications.Test.Redis.Unit
{
    /// <summary>
    /// Unit tests for RedisStreamParser.
    /// Tests parsing of Redis XREADGROUP RESP2 protocol responses into StreamEntry objects.
    /// </summary>
    [TestClass]
    [TestCategory("UnitTest")]
    public sealed class RedisStreamParserTests
    {
        private RedisStreamParser _parser = null!;

        [TestInitialize]
        public void Setup()
        {
            _parser = new RedisStreamParser();
        }

        #region ParseStreamEntries - Basic Tests

        [TestMethod]
        public void ParseStreamEntries_WithValidResponse_ShouldReturnEntries()
        {
            // Arrange
            var result = CreateMockXReadGroupResult(
                streamName: "notifications:stream",
                entries: new[]
                {
                    CreateMockEntry("1234567890-0", new[] { "key", "notifications:1", "type", "TestType" })
                }
            );

            // Act
            var entries = _parser.ParseStreamEntries(result).ToList();

            // Assert
            Assert.AreEqual(1, entries.Count, "Should parse one entry");
            Assert.AreEqual("1234567890-0", entries[0].Id.ToString());
            Assert.AreEqual(2, entries[0].Values.Length, "Should have 2 field-value pairs");
        }

        [TestMethod]
        public void ParseStreamEntries_WithNullResult_ShouldReturnEmptyList()
        {
            // Arrange
            var result = RedisResult.Create((RedisValue[])null!);

            // Act
            var entries = _parser.ParseStreamEntries(result).ToList();

            // Assert
            Assert.AreEqual(0, entries.Count, "Null result should return empty list");
        }

        [TestMethod]
        public void ParseStreamEntries_WithEmptyArray_ShouldReturnEmptyList()
        {
            // Arrange
            var result = RedisResult.Create(Array.Empty<RedisResult>());

            // Act
            var entries = _parser.ParseStreamEntries(result).ToList();

            // Assert
            Assert.AreEqual(0, entries.Count, "Empty array should return empty list");
        }

        #endregion

        #region ParseStreamEntries - Invalid Structure Tests

        [TestMethod]
        public void ParseStreamEntries_WithInvalidStructure_ShouldSkipInvalidEntries()
        {
            // Arrange - Create a response with one valid and one invalid entry
            var validEntry = CreateMockEntry("1234567890-0", new[] { "key", "value" });
            var invalidEntry = RedisResult.Create(new RedisValue("invalid")); // Not an array

            var result = RedisResult.Create(new RedisResult[]
            {
                RedisResult.Create(new RedisResult[]
                {
                    RedisResult.Create(new RedisValue("notifications:stream")),
                    RedisResult.Create(new RedisResult[] { validEntry, invalidEntry })
                })
            });

            // Act
            var entries = _parser.ParseStreamEntries(result).ToList();

            // Assert
            Assert.AreEqual(1, entries.Count, "Should skip invalid entry and return only valid one");
            Assert.AreEqual("1234567890-0", entries[0].Id.ToString());
        }

        [TestMethod]
        public void ParseStreamEntries_WithMissingStreamData_ShouldReturnEmpty()
        {
            // Arrange - Stream with no entry data
            var result = RedisResult.Create(new RedisResult[]
            {
                RedisResult.Create(new RedisResult[]
                {
                    RedisResult.Create(new RedisValue("notifications:stream"))
                    // Missing entries array
                })
            });

            // Act
            var entries = _parser.ParseStreamEntries(result).ToList();

            // Assert
            Assert.AreEqual(0, entries.Count, "Should return empty for malformed stream data");
        }

        #endregion

        #region ParseStreamEntries - Multiple Streams Tests

        [TestMethod]
        public void ParseStreamEntries_WithMultipleStreams_ShouldParseAll()
        {
            // Arrange - Two streams with entries
            var stream1Entry = CreateMockEntry("1111111111-0", new[] { "key", "value1" });
            var stream2Entry = CreateMockEntry("2222222222-0", new[] { "key", "value2" });

            var result = RedisResult.Create(new RedisResult[]
            {
                // Stream 1
                RedisResult.Create(new RedisResult[]
                {
                    RedisResult.Create(new RedisValue("notifications:stream:1")),
                    RedisResult.Create(new RedisResult[] { stream1Entry })
                }),
                // Stream 2
                RedisResult.Create(new RedisResult[]
                {
                    RedisResult.Create(new RedisValue("notifications:stream:2")),
                    RedisResult.Create(new RedisResult[] { stream2Entry })
                })
            });

            // Act
            var entries = _parser.ParseStreamEntries(result).ToList();

            // Assert
            Assert.AreEqual(2, entries.Count, "Should parse entries from both streams");
            Assert.AreEqual("1111111111-0", entries[0].Id.ToString());
            Assert.AreEqual("2222222222-0", entries[1].Id.ToString());
        }

        [TestMethod]
        public void ParseStreamEntries_WithMultipleEntriesInOneStream_ShouldParseAll()
        {
            // Arrange - One stream with multiple entries
            var entry1 = CreateMockEntry("1111111111-0", new[] { "key", "value1" });
            var entry2 = CreateMockEntry("2222222222-0", new[] { "key", "value2" });
            var entry3 = CreateMockEntry("3333333333-0", new[] { "key", "value3" });

            var result = CreateMockXReadGroupResult(
                streamName: "notifications:stream",
                entries: new[] { entry1, entry2, entry3 }
            );

            // Act
            var entries = _parser.ParseStreamEntries(result).ToList();

            // Assert
            Assert.AreEqual(3, entries.Count, "Should parse all entries from single stream");
            Assert.AreEqual("1111111111-0", entries[0].Id.ToString());
            Assert.AreEqual("2222222222-0", entries[1].Id.ToString());
            Assert.AreEqual("3333333333-0", entries[2].Id.ToString());
        }

        #endregion

        #region Field-Value Parsing Tests

        [TestMethod]
        public void ParseStreamEntries_WithFieldValuePairs_ShouldParseCorrectly()
        {
            // Arrange
            var result = CreateMockXReadGroupResult(
                streamName: "notifications:stream",
                entries: new[]
                {
                    CreateMockEntry("1234567890-0", new[]
                    {
                        "key", "notifications:abc123",
                        "type", "TestNotificationEntity",
                        "id", "abc123",
                        "timestamp", "2025-01-17T12:00:00Z"
                    })
                }
            );

            // Act
            var entries = _parser.ParseStreamEntries(result).ToList();

            // Assert
            Assert.AreEqual(1, entries.Count);
            Assert.AreEqual(4, entries[0].Values.Length, "Should have 4 field-value pairs");

            var values = entries[0].Values;
            Assert.AreEqual("key", values[0].Name.ToString());
            Assert.AreEqual("notifications:abc123", values[0].Value.ToString());
            Assert.AreEqual("type", values[1].Name.ToString());
            Assert.AreEqual("TestNotificationEntity", values[1].Value.ToString());
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Creates a mock XREADGROUP result structure.
        /// Format: [[stream_name, [[entry_id, [field1, value1, field2, value2, ...]]]]]
        /// </summary>
        private RedisResult CreateMockXReadGroupResult(string streamName, RedisResult[] entries)
        {
            return RedisResult.Create(new RedisResult[]
            {
                RedisResult.Create(new RedisResult[]
                {
                    RedisResult.Create(new RedisValue(streamName)),
                    RedisResult.Create(entries)
                })
            });
        }

        /// <summary>
        /// Creates a mock stream entry.
        /// Format: [entry_id, [field1, value1, field2, value2, ...]]
        /// </summary>
        private RedisResult CreateMockEntry(string entryId, string[] fieldValuePairs)
        {
            if (fieldValuePairs.Length % 2 != 0)
                throw new ArgumentException("Field-value pairs must be even", nameof(fieldValuePairs));

            var fields = new RedisValue[fieldValuePairs.Length];
            for (int i = 0; i < fieldValuePairs.Length; i++)
            {
                fields[i] = new RedisValue(fieldValuePairs[i]);
            }

            return RedisResult.Create(new RedisResult[]
            {
                RedisResult.Create(new RedisValue(entryId)),
                RedisResult.Create(fields)
            });
        }

        #endregion
    }
}
