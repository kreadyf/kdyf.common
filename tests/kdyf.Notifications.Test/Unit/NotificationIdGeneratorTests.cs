using kdyf.Notifications.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace kdyf.Notifications.Test.Unit
{
    /// <summary>
    /// Tests for NotificationIdGenerator helper class.
    /// </summary>
    [TestClass]
    public class NotificationIdGeneratorTests
    {
        #region Test Models

        /// <summary>
        /// Test notification implementing INotificationEntity.
        /// </summary>
        private class TestNotification : INotificationEntity
        {
            public string NotificationId { get; set; } = string.Empty;
            public DateTime Timestamp { get; set; }
            public string NotificationType { get; set; } = string.Empty;
            public string? GroupId { get; set; }
            public LogLevel Level { get; set; }
            public string Message { get; set; } = string.Empty;
            public HashSet<string> Tags { get; set; } = new HashSet<string>();

            public string OrderId { get; set; } = string.Empty;
            public decimal Amount { get; set; }
        }

        #endregion

    }
}
