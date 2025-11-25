using StackExchange.Redis;

namespace kdyf.Notifications.Redis.Services
{
    /// <summary>
    /// Service responsible for parsing Redis RESP2 protocol responses into typed stream entries.
    /// Separates protocol parsing logic from business logic in receivers.
    /// </summary>
    public class RedisStreamParser
    {
        /// <summary>
        /// Parses XREADGROUP response into StreamEntry collection.
        /// Handles RESP2 array protocol from Redis streams.
        /// </summary>
        /// <param name="result">Redis result from XREADGROUP command.</param>
        /// <returns>Collection of parsed stream entries.</returns>
        /// <remarks>
        /// Redis XREADGROUP returns:
        /// Array[
        ///   Array[
        ///     "stream_name",
        ///     Array[
        ///       Array["entry_id", Array["field1", "value1", "field2", "value2", ...]]
        ///     ]
        ///   ]
        /// ]
        /// </remarks>
        public IEnumerable<StreamEntry> ParseStreamEntries(RedisResult result)
        {
            var entries = new List<StreamEntry>();

            // Validate top-level result
            if (result.IsNull || result.Resp2Type != ResultType.Array)
                return entries;

            var streams = (RedisResult[])result!;
            if (streams == null || streams.Length == 0)
                return entries;

            // Iterate through each stream in response
            foreach (var stream in streams)
            {
                if (stream.IsNull || stream.Resp2Type != ResultType.Array)
                    continue;

                var streamData = (RedisResult[])stream!;
                if (streamData == null || streamData.Length < 2)
                    continue;

                // streamData[0] = stream name (not needed)
                // streamData[1] = array of entries
                if (streamData[1].IsNull || streamData[1].Resp2Type != ResultType.Array)
                    continue;

                var streamEntries = (RedisResult[])streamData[1]!;
                if (streamEntries == null)
                    continue;

                // Parse each entry in the stream
                foreach (var entryResult in streamEntries)
                {
                    var entry = ParseSingleEntry(entryResult);
                    if (entry.HasValue)
                    {
                        entries.Add(entry.Value);
                    }
                }
            }

            return entries;
        }

        /// <summary>
        /// Parses a single stream entry from Redis result.
        /// </summary>
        /// <param name="entryResult">Redis result representing a single entry.</param>
        /// <returns>Parsed StreamEntry if valid, null otherwise.</returns>
        private StreamEntry? ParseSingleEntry(RedisResult entryResult)
        {
            if (entryResult.IsNull || entryResult.Resp2Type != ResultType.Array)
                return null;

            var entryData = (RedisResult[])entryResult!;
            if (entryData == null || entryData.Length < 2)
                return null;

            // entryData[0] = entry ID (e.g., "1234567890-0")
            var messageId = entryData[0].ToString();
            if (string.IsNullOrEmpty(messageId))
                return null;

            // entryData[1] = array of field-value pairs
            if (entryData[1].IsNull || entryData[1].Resp2Type != ResultType.Array)
                return null;

            var fields = (RedisResult[])entryData[1]!;
            if (fields == null)
                return null;

            // Parse field-value pairs
            var nameValues = ParseFieldValuePairs(fields);
            if (nameValues.Count == 0)
                return null;

            return new StreamEntry(messageId, nameValues.ToArray());
        }

        /// <summary>
        /// Parses Redis field-value pairs into NameValueEntry collection.
        /// Redis stores fields as flat array: [field1, value1, field2, value2, ...]
        /// </summary>
        /// <param name="fields">Redis result array containing alternating field names and values.</param>
        /// <returns>List of parsed NameValueEntry.</returns>
        private List<NameValueEntry> ParseFieldValuePairs(RedisResult[] fields)
        {
            var nameValues = new List<NameValueEntry>();

            // Fields come in pairs: [name1, value1, name2, value2, ...]
            for (int i = 0; i < fields.Length - 1; i += 2)
            {
                var fieldName = fields[i].ToString();
                var fieldValue = fields[i + 1].ToString();

                if (!string.IsNullOrEmpty(fieldName))
                {
                    nameValues.Add(new NameValueEntry(fieldName, fieldValue ?? ""));
                }
            }

            return nameValues;
        }
    }
}
