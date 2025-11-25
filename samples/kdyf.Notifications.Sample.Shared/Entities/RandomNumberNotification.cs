using kdyf.Notifications.Interfaces;
using Microsoft.Extensions.Logging;

namespace kdyf.Notifications.Sample.Shared.Entities;

/// <summary>
/// Shared notification containing a random number.
/// Used by both Sample01 and Sample02 to avoid cross-assembly type mismatch issues.
/// </summary>
public class RandomNumberNotification : INotificationEntity
{
    public string NotificationId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string NotificationType { get; set; } = nameof(RandomNumberNotification);
    public string? GroupId { get; set; }
    public LogLevel Level { get; set; } = LogLevel.Information;
    public string Message { get; set; } = string.Empty;
    public HashSet<string> Tags { get; set; } = new();

    // Business-specific properties
    public int RandomValue { get; set; }
    public string Source { get; set; } = string.Empty;
}

