using kdyf.Notifications.Interfaces;
using kdyf.Notifications.Redis.Configuration;
using kdyf.Notifications.Redis.Resilience;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace kdyf.Notifications.Redis.Services
{
    /// <summary>
    /// Handles low-level Redis operations for notification emission.
    /// Encapsulates retry logic, stream maintenance, and TTL management.
    /// This is a stateless service that receives IDatabase as a parameter.
    /// </summary>
    internal class RedisOperationsHandler
    {
        private readonly IRetryPolicy _retryPolicy;
        private readonly RedisNotificationOptions? _options;
        private readonly ILogger _logger;

        public RedisOperationsHandler(
            IRetryPolicy retryPolicy,
            RedisNotificationOptions? options,
            ILogger logger)
        {
            _retryPolicy = retryPolicy ?? throw new ArgumentNullException(nameof(retryPolicy));
            _options = options;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Emits a stream-only notification (no separate key-value storage).
        /// Stores full JSON payload in the stream entry.
        /// </summary>
        public async Task EmitStreamOnlyAsync(
            IDatabase db,
            INotificationEntity entity,
            string json,
            string typeName,
            string streamName)
        {
            var fields = new NameValueEntry[]
            {
                new("payload", json),
                new("type", typeName),
                new("id", entity.NotificationId.ToString()),
                new("timestamp", entity.Timestamp.ToString("O")),
                new("storage", "stream-only")
            };

            // Add to stream with automatic trimming and TTL
            await AddToStreamWithMaintenanceAsync(db, streamName, fields, entity.GetType());

            _logger.LogTrace("Stream-only notification {Id} emitted to {StreamName} (no key-value storage).",
                entity.NotificationId, streamName);
        }

        /// <summary>
        /// Emits an updateable notification using a custom update key.
        /// Updates the same Redis key for all notifications with the same update key.
        /// </summary>
        public async Task EmitUpdateableAsync(
            IDatabase db,
            INotificationEntity entity,
            string json,
            string typeName,
            string updateKey,
            long? sequence,
            string streamName,
            CancellationToken cancellationToken)
        {
            var redisKey = $"notifications:updateable:{updateKey}";

            // Get TTL for this notification type (use default, per-type TTL removed)
            var messageTTL = _options?.Storage.MessageTTL ?? TimeSpan.FromHours(1);

            // Save/update payload with expiration and automatic retry
            await SetRedisKeyWithRetryAsync(db, redisKey, json, messageTTL, cancellationToken);

            var fields = new System.Collections.Generic.List<NameValueEntry>
            {
                new("key", redisKey),
                new("type", typeName),
                new("id", entity.NotificationId.ToString()),
                new("timestamp", entity.Timestamp.ToString("O")),
                new("updateKey", updateKey),
                new("storage", "updateable")
            };

            if (sequence.HasValue)
            {
                fields.Add(new NameValueEntry("sequence", sequence.Value.ToString()));
            }

            // Add to stream with automatic trimming and TTL
            await AddToStreamWithMaintenanceAsync(db, streamName, fields.ToArray(), entity.GetType());

            _logger.LogTrace("Updateable notification {Id} emitted to {StreamName} with updateKey={UpdateKey}, sequence={Sequence}.",
                entity.NotificationId, streamName, updateKey, sequence?.ToString() ?? "none");
        }

        /// <summary>
        /// Emits a standard notification with key-value storage + stream reference.
        /// </summary>
        public async Task EmitStandardAsync(
            IDatabase db,
            INotificationEntity entity,
            string json,
            string typeName,
            string streamName,
            CancellationToken cancellationToken)
        {
            var key = $"notifications:{entity.NotificationId}";

            // Get TTL for this notification type (use default, per-type TTL removed)
            var messageTTL = _options?.Storage.MessageTTL ?? TimeSpan.FromHours(1);

            // Save payload to Redis with expiration and automatic retry
            await SetRedisKeyWithRetryAsync(db, key, json, messageTTL, cancellationToken);

            var fields = new NameValueEntry[]
            {
                new("key", key),
                new("type", typeName),
                new("id", entity.NotificationId.ToString()),
                new("timestamp", entity.Timestamp.ToString("O"))
            };

            // Add to stream with automatic trimming and TTL
            await AddToStreamWithMaintenanceAsync(db, streamName, fields, entity.GetType());
        }

        /// <summary>
        /// Adds an entry to a Redis stream with automatic trimming and TTL extension.
        /// </summary>
        private async Task<RedisValue> AddToStreamWithMaintenanceAsync(
            IDatabase db,
            string streamName,
            NameValueEntry[] fields,
            Type entityType)
        {
            // Add to stream with trimming
            var maxLength = _options?.Storage.MaxStreamLength ?? 10000;
            RedisValue entryId;

            if (maxLength > 0)
            {
                // Add with automatic trimming
                entryId = await db.StreamAddAsync(
                    streamName,
                    fields,
                    messageId: null,
                    maxLength: maxLength,
                    useApproximateMaxLength: _options?.Storage.UseApproximateTrimming ?? false);

                _logger.LogTrace(
                    "Added entry to stream {StreamName} with trimming (maxLength: {MaxLength}, approximate: {Approximate})",
                    streamName,
                    maxLength,
                    _options?.Storage.UseApproximateTrimming ?? false);
            }
            else
            {
                // Trimming disabled
                entryId = await db.StreamAddAsync(streamName, fields);
                _logger.LogTrace("Added entry to stream {StreamName} without trimming", streamName);
            }

            // Extend stream TTL (use default, per-type TTL removed)
            var streamTTL = _options?.Storage.StreamTTL ?? TimeSpan.FromHours(24);
            if (streamTTL > TimeSpan.Zero)
            {
                await db.KeyExpireAsync(streamName, streamTTL);
                _logger.LogTrace("Extended stream TTL: {StreamName} → {TTL}", streamName, streamTTL);
            }

            return entryId;
        }

        /// <summary>
        /// Sets a Redis key using the injected retry policy for transient connection failures.
        /// Delegates retry logic to IRetryPolicy for better separation of concerns.
        /// </summary>
        /// <param name="db">Redis database instance.</param>
        /// <param name="key">Redis key to set.</param>
        /// <param name="value">Value to store.</param>
        /// <param name="expiry">Key expiration time.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        private async Task SetRedisKeyWithRetryAsync(
            IDatabase db,
            string key,
            string value,
            TimeSpan expiry,
            CancellationToken cancellationToken)
        {
            await _retryPolicy.ExecuteAsync(
                async () => await db.StringSetAsync(key, value, expiry),
                cancellationToken);
        }
    }
}
