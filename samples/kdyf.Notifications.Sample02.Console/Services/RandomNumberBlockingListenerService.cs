using kdyf.Notifications.Interfaces;
using kdyf.Notifications.Sample.Shared.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Reactive.Concurrency;
using System.Reactive.Linq;

namespace kdyf.Notifications.Sample02.Console.Services;

/// <summary>
/// Background service that listens to RandomNumber notifications with 1-second blocking delay.
/// Uses RX ObserveOn to ensure it doesn't block emission or other listeners.
/// </summary>
public class RandomNumberBlockingListenerService : BackgroundService
{
    private readonly INotificationReceiver _receiver;
    private readonly ILogger<RandomNumberBlockingListenerService> _logger;
    private IDisposable? _subscription;

    public RandomNumberBlockingListenerService(
        INotificationReceiver receiver,
        ILogger<RandomNumberBlockingListenerService> logger)
    {
        _receiver = receiver;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("RandomNumberBlockingListenerService started");

        _subscription = _receiver
            .Receive<RandomNumberNotification>(stoppingToken)
            .ObserveOn(TaskPoolScheduler.Default) // Process on background thread
            .Subscribe(
                async notification =>
                {
                    _logger.LogInformation("[BLOCKING LISTENER] Received RandomNumber: {Value} (Source: {Source})", 
                        notification.RandomValue, notification.Source);
                    await Task.Delay(1000, stoppingToken); // Block for 1 second, but on background thread
                    _logger.LogInformation("[BLOCKING LISTENER] Processed RandomNumber: {Value}", notification.RandomValue);
                },
                error => _logger.LogError(error, "Error in RandomNumberBlockingListenerService"),
                () => _logger.LogInformation("RandomNumberBlockingListenerService completed"));

        return Task.CompletedTask;
    }

    public override void Dispose()
    {
        _subscription?.Dispose();
        base.Dispose();
    }
}

