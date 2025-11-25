using kdyf.Notifications.Interfaces;
using Microsoft.Extensions.Logging;

namespace kdyf.Notifications.Sample02.Console.Entities;

/// <summary>
/// Notification for webhook events with large payload (4-8kb).
/// </summary>
public class WebhookNotification : INotificationEntity
{
    public string NotificationId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string NotificationType { get; set; } = nameof(WebhookNotification);
    public string? GroupId { get; set; }
    public LogLevel Level { get; set; } = LogLevel.Information;
    public string Message { get; set; } = string.Empty;
    public HashSet<string> Tags { get; set; } = new();

    // Business-specific properties
    public string WebhookId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty; // Large payload 4-8kb
    public Dictionary<string, object> Metadata { get; set; } = new();
}

