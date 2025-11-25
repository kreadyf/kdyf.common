using kdyf.Notifications.Interfaces;
using kdyf.Notifications.Redis.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace kdyf.Notifications.Redis.Services
{
    /// <summary>
    /// Processes notification messages from a channel in the background.
    /// Coordinates strategy selection, serialization, and Redis operations.
    /// </summary>
    internal class NotificationChannelProcessor
    {
        private readonly IConnectionMultiplexer _redis;
        private readonly RedisNotificationOptions? _options;
        private readonly ILogger _logger;
        private readonly RedisOperationsHandler _redisOpsHandler;

        public NotificationChannelProcessor(
            IConnectionMultiplexer redis,
            RedisNotificationOptions? options,
            ILogger logger,
            RedisOperationsHandler redisOpsHandler)
        {
            _redis = redis ?? throw new ArgumentNullException(nameof(redis));
            _options = options;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _redisOpsHandler = redisOpsHandler ?? throw new ArgumentNullException(nameof(redisOpsHandler));
        }

        /// <summary>
        /// Background loop that reads from the channel and emits notifications to Redis.
        /// </summary>
        public async Task ProcessChannelAsync(
            ChannelReader<NotificationMessage> channelReader,
            string baseStreamName,
            CancellationToken cancellationToken)
        {
            _logger.LogInformation("Channel processor started. Reading notifications from channel...");

            try
            {
                await foreach (var message in channelReader.ReadAllAsync(cancellationToken))
                {
                    try
                    {
                        await ProcessMessageAsync(message, baseStreamName, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            "Error processing notification {NotificationId} of type {Type} from channel. " +
                            "Notification queued at {QueuedTime}, age: {Age}ms",
                            message.Entity.NotificationId,
                            message.EntityType.Name,
                            message.QueuedTimestamp,
                            (DateTime.UtcNow - message.QueuedTimestamp).TotalMilliseconds);

                        // Continue processing - don't let one failure stop the channel
                        // In production, consider implementing a dead-letter queue here
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Channel processor cancelled.");
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "Channel processor encountered a fatal error.");
            }

            _logger.LogInformation("Channel processor stopped.");
        }

        /// <summary>
        /// Processes a single notification message from the channel.
        /// </summary>
        private async Task ProcessMessageAsync(
            NotificationMessage message,
            string baseStreamName,
            CancellationToken cancellationToken)
        {
            var entity = message.Entity;
            var entityType = message.EntityType;

            // Serialize the payload
            var json = JsonSerializer.Serialize(entity, entityType, new JsonSerializerOptions
            {
                WriteIndented = false,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            // Use AssemblyQualifiedName for cross-process compatibility and reliable Type.GetType() resolution
            // Falls back to FullName for backward compatibility, then to NotificationType property
            var typeName = entityType.AssemblyQualifiedName ?? entityType.FullName ?? entity.NotificationType ?? "Unknown";
            
            // Get stream name for this type
            var streamName = _options?.TypeToStreamMapping.TryGetValue(entityType, out var mappedStream) == true
                ? mappedStream
                : baseStreamName;

            // Execute Redis operations
            try
            {
                var db = _redis.GetDatabase();

                // Determine strategy and execute
                // Strategy 1: Stream-only storage (no key-value)
                if (_options?.StreamOnlyTypes.Contains(entityType) == true)
                {
                    await _redisOpsHandler.EmitStreamOnlyAsync(db, entity, json, typeName, streamName);
                }
                // Strategy 2: Updateable messages with custom key
                else if (_options?.UpdateableTypes.TryGetValue(entityType, out var updateConfig) == true && updateConfig != null)
                {
                    var updateKey = updateConfig.UpdateKeyExtractor(entity);
                    if (!string.IsNullOrWhiteSpace(updateKey))
                    {
                        await _redisOpsHandler.EmitUpdateableAsync(
                            db, entity, json, typeName, updateKey,
                            updateConfig.SequenceExtractor?.Invoke(entity),
                            streamName, cancellationToken);
                    }
                    else
                    {
                        // Fallback to standard if updateable config is invalid
                        await _redisOpsHandler.EmitStandardAsync(db, entity, json, typeName, streamName, cancellationToken);
                    }
                }
                // Strategy 3: Standard behavior (key-value + stream reference)
                else
                {
                    await _redisOpsHandler.EmitStandardAsync(db, entity, json, typeName, streamName, cancellationToken);
                }

                var age = (DateTime.UtcNow - message.QueuedTimestamp).TotalMilliseconds;
                _logger.LogDebug("Notification {Id} of type {Type} emitted to Redis stream {StreamName}. Queue age: {Age}ms",
                    entity.NotificationId, entityType.Name, streamName, age);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to emit notification {Id} to Redis stream {StreamName}.",
                    entity.NotificationId, streamName);
                throw;
            }
        }
    }

    /// <summary>
    /// Represents a notification message in the fire-and-forget channel.
    /// </summary>
    internal class NotificationMessage
    {
        public INotificationEntity Entity { get; set; } = null!;
        public Type EntityType { get; set; } = null!;
        public DateTime QueuedTimestamp { get; set; }
    }
}
