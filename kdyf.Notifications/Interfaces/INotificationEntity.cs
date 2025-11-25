using Microsoft.Extensions.Logging;

namespace kdyf.Notifications.Interfaces
{
    /// <summary>
    /// Defines the base contract for notification entities with metadata and identification.
    /// </summary>
    public interface INotificationEntity
    {
        /// <summary>
        /// Gets or sets the unique identifier for this notification.
        /// Can be a business ID (e.g., "ORDER-12345") or auto-generated GUID string.
        /// </summary>
        public string NotificationId { get; set; }

        /// <summary>
        /// Gets or sets the timestamp when the notification was created.
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// Gets or sets the type of the notification.
        /// </summary>
        public string NotificationType { get; set; }

        /// <summary>
        /// Gets or sets an optional group identifier for related notifications.
        /// </summary>
        public string? GroupId { get; set; }

        /// <summary>
        /// Gets or sets the log level of the notification (Warn, Error, Info, Debug).
        /// </summary>
        public LogLevel Level { get; set; }

        /// <summary>
        /// Gets or sets the message content of the notification.
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// Gets or sets the tags associated with this notification for filtering purposes.
        /// </summary>
        public HashSet<string> Tags { get; set; }
    }
}
