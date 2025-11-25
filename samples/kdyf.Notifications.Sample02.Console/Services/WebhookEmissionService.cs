using kdyf.Notifications.Interfaces;
using kdyf.Notifications.Sample02.Console.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text;

namespace kdyf.Notifications.Sample02.Console.Services;

/// <summary>
/// Background service that emits Webhook notifications randomly between 1-2 seconds with large payload (4-8kb).
/// </summary>
public class WebhookEmissionService : BackgroundService
{
    private readonly INotificationEmitter _emitter;
    private readonly ILogger<WebhookEmissionService> _logger;

    public WebhookEmissionService(
        INotificationEmitter emitter,
        ILogger<WebhookEmissionService> logger)
    {
        _emitter = emitter;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("WebhookEmissionService started");

        var random = new Random();
        int count = 0;

        while (!stoppingToken.IsCancellationRequested && count < 10)
        {
            var delay = random.Next(1000, 2001); // 1-2 seconds
            await Task.Delay(delay, stoppingToken);

            // Generate large payload (4-8kb)
            var payloadSize = random.Next(4000, 8001); // 4-8kb
            var payload = GenerateLargePayload(payloadSize);

            var notification = new WebhookNotification
            {
                WebhookId = $"webhook-{Guid.NewGuid():N}",
                EventType = "sample.event",
                Payload = payload,
                Metadata = new Dictionary<string, object>
                {
                    ["timestamp"] = DateTime.UtcNow,
                    ["source"] = "Sample02",
                    ["payloadSize"] = payloadSize
                },
                Tags = new HashSet<string> { "webhook", "large-payload" }
            };

            // Measure actual emission time
            var emitStart = DateTime.UtcNow;
            await _emitter.NotifyAsync(notification, stoppingToken);
            var emitDuration = (DateTime.UtcNow - emitStart).TotalMilliseconds;

            _logger.LogInformation(
                "[EMITTED] Webhook: {WebhookId} (payload: {PayloadSize} bytes, wait delay: {WaitDelay}ms, emit time: {EmitTime}ms)",
                notification.WebhookId, payloadSize, delay, emitDuration);
            count++;
        }

        _logger.LogInformation("WebhookEmissionService completed");
    }

    private static string GenerateLargePayload(int sizeInBytes)
    {
        var sb = new StringBuilder(sizeInBytes);
        var random = new Random();
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 ";
        
        for (int i = 0; i < sizeInBytes; i++)
        {
            sb.Append(chars[random.Next(chars.Length)]);
        }
        
        return sb.ToString();
    }
}




