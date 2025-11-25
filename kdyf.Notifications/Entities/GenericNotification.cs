using kdyf.Notifications.Interfaces;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace kdyf.Notifications.Entities
{
    /// <summary>
    /// Generic notification entity used as a fallback when the specific notification type cannot be resolved.
    /// Preserves the raw JSON payload for potential manual processing or logging.
    /// </summary>
    public class GenericNotification : INotificationEntity
    {
        /// <summary>
        /// Gets or sets the unique identifier for this notification.
        /// Can be a business ID (e.g., "ORDER-12345") or auto-generated GUID string.
        /// </summary>
        public string NotificationId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the timestamp when the notification was created.
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// Gets or sets the fully qualified type name of the original notification.
        /// </summary>
        public string NotificationType { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets an optional group identifier for related notifications.
        /// </summary>
        public string? GroupId { get; set; }

        /// <summary>
        /// Gets or sets the log level of the notification.
        /// </summary>
        public LogLevel Level { get; set; } = LogLevel.Information;

        /// <summary>
        /// Gets or sets the message content of the notification.
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets tags for filtering and categorization.
        /// </summary>
        public HashSet<string> Tags { get; set; } = new();

        /// <summary>
        /// Gets or sets the raw JSON data from the original notification.
        /// This preserves all properties even when the type cannot be resolved.
        /// </summary>
        public JsonElement Data { get; set; }

        /// <summary>
        /// Gets a value indicating whether this is a generic fallback notification.
        /// Always returns true for GenericNotification instances.
        /// </summary>
        public bool IsGenericFallback => true;

        /// <summary>
        /// Creates a new instance of GenericNotification.
        /// </summary>
        public GenericNotification()
        {
        }

        /// <summary>
        /// Creates a new instance of GenericNotification with the specified properties.
        /// </summary>
        /// <param name="notificationId">The unique identifier for the notification.</param>
        /// <param name="timestamp">The timestamp when the notification was created.</param>
        /// <param name="notificationType">The fully qualified type name of the original notification.</param>
        /// <param name="data">The raw JSON data from the original notification.</param>
        /// <param name="tags">Optional tags for filtering.</param>
        public GenericNotification(
            string notificationId,
            DateTime timestamp,
            string notificationType,
            JsonElement data,
            HashSet<string>? tags = null)
        {
            NotificationId = notificationId;
            Timestamp = timestamp;
            NotificationType = notificationType;
            Data = data;
            Tags = tags ?? new HashSet<string>();
        }

        /// <summary>
        /// Gets the raw JSON string representation of the data.
        /// </summary>
        /// <returns>A JSON string representation of the data.</returns>
        public string GetRawJson()
        {
            return Data.GetRawText();
        }

        /// <summary>
        /// Attempts to deserialize the data to a specific type.
        /// </summary>
        /// <typeparam name="T">The type to deserialize to.</typeparam>
        /// <param name="options">Optional JSON serializer options.</param>
        /// <returns>The deserialized object, or default(T) if deserialization fails.</returns>
        public T? TryDeserialize<T>(JsonSerializerOptions? options = null)
        {
            try
            {
                return Data.Deserialize<T>(options);
            }
            catch
            {
                return default;
            }
        }

        public override string ToString()
        {
            return $"GenericNotification [Id={NotificationId}, Type={NotificationType}, Timestamp={Timestamp:O}]";
        }
    }
}
