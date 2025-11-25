using kdyf.Notifications.Interfaces;
using kdyf.Notifications.Sample.Shared.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Reactive.Concurrency;
using System.Reactive.Linq;

namespace kdyf.Notifications.Sample02.Console.Services;

/// <summary>
/// Background service that listens to RandomNumber notifications with no blocking.
/// </summary>
public class RandomNumberNonBlockingListenerService : BackgroundService
{
    private readonly INotificationReceiver _receiver;
    private readonly ILogger<RandomNumberNonBlockingListenerService> _logger;
    private IDisposable? _subscription;

    public RandomNumberNonBlockingListenerService(
        INotificationReceiver receiver,
        ILogger<RandomNumberNonBlockingListenerService> logger)
    {
        _receiver = receiver;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("RandomNumberNonBlockingListenerService started");

        _subscription = _receiver
            .Receive<RandomNumberNotification>(stoppingToken)
            .ObserveOn(TaskPoolScheduler.Default)
            .Subscribe(
                notification =>
                {
                    _logger.LogInformation("[NON-BLOCKING LISTENER] Received RandomNumber: {Value} (Source: {Source})", 
                        notification.RandomValue, notification.Source);
                },
                error => _logger.LogError(error, "Error in RandomNumberNonBlockingListenerService"),
                () => _logger.LogInformation("RandomNumberNonBlockingListenerService completed"));

        return Task.CompletedTask;
    }

    public override void Dispose()
    {
        _subscription?.Dispose();
        base.Dispose();
    }
}

