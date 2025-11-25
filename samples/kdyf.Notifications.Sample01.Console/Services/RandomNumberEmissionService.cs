using kdyf.Notifications.Interfaces;
using kdyf.Notifications.Sample.Shared.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace kdyf.Notifications.Sample01.Console.Services;

/// <summary>
/// Background service that emits RandomNumber notifications randomly between 200ms-400ms.
/// </summary>
public class RandomNumberEmissionService : BackgroundService
{
    private readonly INotificationEmitter _emitter;
    private readonly ILogger<RandomNumberEmissionService> _logger;

    public RandomNumberEmissionService(
        INotificationEmitter emitter,
        ILogger<RandomNumberEmissionService> logger)
    {
        _emitter = emitter;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(5000);
        _logger.LogInformation("RandomNumberEmissionService started");

        var random = new Random();
        int count = 0;

        while (!stoppingToken.IsCancellationRequested && count < 5)
        {
            var delay = random.Next(200, 401); // 200-400ms
            await Task.Delay(delay, stoppingToken);

            var notification = new RandomNumberNotification
            {
                RandomValue = random.Next(1, 1000),
                Source = "Sample01",
                Tags = new HashSet<string> { "random", "stream-only" }
            };

            await _emitter.NotifyAsync(notification, stoppingToken);
            count++;
            _logger.LogTrace("[Notification] NotificationId: {Notification}", notification.NotificationId);
            _logger.LogInformation("[EMITTED] RandomNumber: {Value} (Count: {Count}, Delay: {Delay}ms)", notification.RandomValue, count, delay);
        }

        _logger.LogInformation("RandomNumberEmissionService completed after {Count} emissions", count);
    }
}

