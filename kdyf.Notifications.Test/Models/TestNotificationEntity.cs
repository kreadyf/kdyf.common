using kdyf.Notifications.Interfaces;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace kdyf.Notifications.Test.Models
{
    /// <summary>
    /// Test notification entity for unit testing purposes.
    /// </summary>
    public class TestNotificationEntity : INotificationEntity
    {
        /// <summary>
        /// Gets or sets the unique identifier for this notification.
        /// </summary>
        public string NotificationId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the timestamp when the notification was created.
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// Gets or sets the type of the notification.
        /// </summary>
        public string NotificationType { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the message content for testing.
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets an optional group identifier for related notifications.
        /// </summary>
        public string? GroupId { get; set; }

        /// <summary>
        /// Gets or sets the log level of the notification.
        /// </summary>
        public LogLevel Level { get; set; }

        /// <summary>
        /// Gets or sets the tags associated with this notification.
        /// </summary>
        public HashSet<string> Tags { get; set; } = new HashSet<string>();

    }
}
