using kdyf.Notifications.Entities;
using kdyf.Notifications.Interfaces;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace kdyf.Notifications.Services
{
    /// <summary>
    /// Service responsible for resolving notification types from type names.
    /// Uses Type.GetType with version-agnostic fallback for backward compatibility.
    /// </summary>
    public class NotificationTypeResolver
    {
        private readonly ILogger<NotificationTypeResolver> _logger;

        /// <summary>
        /// Creates a new instance of the notification type resolver.
        /// </summary>
        /// <param name="logger">Logger for diagnostic messages.</param>
        public NotificationTypeResolver(ILogger<NotificationTypeResolver> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Resolves a type from its name using Type.GetType with version-agnostic fallback.
        /// Tries both original name and version-stripped variants for backward compatibility.
        /// </summary>
        /// <param name="typeName">The name of the type to resolve (can be AssemblyQualifiedName, FullName, or short name).</param>
        /// <returns>The resolved Type, or null if the type cannot be resolved.</returns>
        public Type? ResolveType(string? typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
            {
                _logger.LogWarning("Cannot resolve type: typeName is null or empty");
                return null;
            }

            // Generate type name variants for version-agnostic resolution
            var typeNameVariants = GetTypeNameVariants(typeName);

            // Try Type.GetType for each variant
            foreach (var variant in typeNameVariants)
            {
                var targetType = Type.GetType(variant, throwOnError: false);
                if (targetType != null)
                {
                    return targetType;
                }
            }

            _logger.LogWarning("Could not resolve type '{TypeName}'", typeName);
            return null;
        }

        /// <summary>
        /// Generates type name variants for version-agnostic resolution.
        /// Supports backward compatibility when assembly versions change between sender and receiver.
        /// </summary>
        /// <param name="typeName">The original type name.</param>
        /// <returns>An enumerable of type name variants to try.</returns>
        private static IEnumerable<string> GetTypeNameVariants(string typeName)
        {
            // Variant 1: Original name (handles exact matches)
            yield return typeName;

            // Variant 2: Name without version info (handles backward compatibility)
            // "MyApp.Order, MyApp, Version=1.0.0.0, Culture=neutral" → "MyApp.Order"
            if (typeName.Contains(","))
            {
                yield return typeName.Split(',')[0].Trim();
            }
        }

        /// <summary>
        /// Deserializes JSON into a notification entity, with fallback to GenericNotification.
        /// Centralizes fallback logic for when type resolution or deserialization fails.
        /// </summary>
        /// <param name="json">The JSON payload to deserialize.</param>
        /// <param name="typeName">The type name from the notification.</param>
        /// <param name="notificationId">The notification ID (if available).</param>
        /// <param name="timestamp">The timestamp (if available).</param>
        /// <returns>The deserialized notification entity, or a GenericNotification fallback.</returns>
        public INotificationEntity DeserializeOrCreateFallback(
            string json,
            string typeName,
            string? notificationId = null,
            DateTime? timestamp = null)
        {
            // Try to resolve the type
            Type? targetType = ResolveType(typeName);

            // Fallback: Use GenericNotification if type cannot be resolved
            if (targetType == null)
            {
                // Note: ResolveType() already logged the warning, no need to log again
                return CreateGenericNotificationFallback(json, typeName, notificationId, timestamp);
            }

            // Try to deserialize into the resolved type
            try
            {
                var entity = JsonSerializer.Deserialize(json, targetType, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) as INotificationEntity;

                if (entity == null)
                {
                    _logger.LogWarning("Deserialized object from '{TypeName}' is not an INotificationEntity. Using GenericNotification fallback.", targetType.Name);
                    return CreateGenericNotificationFallback(json, typeName, notificationId, timestamp);
                }

                return entity;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to deserialize notification as {TypeName}. Using GenericNotification fallback.", targetType.Name);
                return CreateGenericNotificationFallback(json, typeName, notificationId, timestamp);
            }
        }

        /// <summary>
        /// Creates a GenericNotification fallback when type resolution or deserialization fails.
        /// Preserves all original data for debugging and manual processing.
        /// </summary>
        /// <param name="json">The JSON payload to preserve in the Data field.</param>
        /// <param name="typeName">The original type name from the notification.</param>
        /// <param name="notificationId">The notification ID (if available).</param>
        /// <param name="timestamp">The timestamp (if available).</param>
        /// <returns>A GenericNotification preserving all original data.</returns>
        private GenericNotification CreateGenericNotificationFallback(
            string json,
            string typeName,
            string? notificationId,
            DateTime? timestamp)
        {
            var jsonDoc = JsonDocument.Parse(json);
            var id = string.IsNullOrWhiteSpace(notificationId) ? Guid.NewGuid().ToString() : notificationId;
            var ts = timestamp ?? DateTime.UtcNow;

            return new GenericNotification
            {
                NotificationId = id,
                Timestamp = ts,
                NotificationType = typeName,
                Data = jsonDoc.RootElement.Clone()
            };
        }
    }
}
