using kdyf.Notifications.Interfaces;
using kdyf.Notifications.Sample.Shared.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Reactive.Concurrency;
using System.Reactive.Linq;

namespace kdyf.Notifications.Sample01.Console.Services;

/// <summary>
/// Background service that listens to all notifications except RandomNumber.
/// </summary>
public class OtherNotificationsListenerService : BackgroundService
{
    private readonly INotificationReceiver _receiver;
    private readonly ILogger<OtherNotificationsListenerService> _logger;
    private IDisposable? _subscription;

    public OtherNotificationsListenerService(
        INotificationReceiver receiver,
        ILogger<OtherNotificationsListenerService> logger)
    {
        _receiver = receiver;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OtherNotificationsListenerService started");

        _subscription = _receiver
            .Receive(stoppingToken)
            .Where(n => n is not RandomNumberNotification)
            .ObserveOn(TaskPoolScheduler.Default)
            .Subscribe(
                notification =>
                {
                    _logger.LogInformation("[OTHER LISTENER] Received type: {Type}", notification.GetType().Name);
                    _logger.LogInformation("[OTHER LISTENER] Payload preview: {Type}", notification.NotificationType);
                },
                error => _logger.LogError(error, "Error in OtherNotificationsListenerService"),
                () => _logger.LogInformation("OtherNotificationsListenerService completed"));

        return Task.CompletedTask;
    }

    public override void Dispose()
    {
        _subscription?.Dispose();
        base.Dispose();
    }
}

