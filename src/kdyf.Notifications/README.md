# kdyf.Notifications

[![NuGet](https://img.shields.io/nuget/v/kdyf.Notifications.svg)](https://www.nuget.org/packages/kdyf.Notifications/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

Reactive notification system for .NET with multi-transport support. Provides a composable notification infrastructure with InMemory (System.Reactive) and extensible transport providers.

## Features

- **Multi-Transport Support**: InMemory (System.Reactive) included, Redis Streams via extension package
- **Reactive Observables**: Built on System.Reactive for powerful stream processing
- **Composite Pattern**: Coordinate multiple transports with centralized deduplication
- **Tag-Based Filtering**: Filter notifications by custom tags
- **Type-Safe Routing**: Strongly-typed notification routing and handling
- **Fire-and-Forget**: Async notification emission without blocking
- **Health Checks**: Built-in health monitoring for all transports
- **Dependency Injection**: Seamless integration with Microsoft.Extensions.DependencyInjection
- **Fluent Builder API**: Intuitive configuration with method chaining
- **Cross-Application Communication**: Share notifications across application boundaries

## Installation

```bash
dotnet add package kdyf.Notifications
```

For Redis transport support:

```bash
dotnet add package kdyf.Notifications.Redis
```

## Quick Start

### 1. Define your notification entity

```csharp
using kdyf.Notifications.Interfaces;

public class OrderCreatedNotification : INotificationEntity
{
    public string NotificationId { get; set; } = Guid.NewGuid().ToString();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string NotificationType { get; set; } = nameof(OrderCreatedNotification);
    public string? GroupId { get; set; }
    public LogLevel Level { get; set; } = LogLevel.Information;
    public string Message { get; set; } = "Order created";
    public HashSet<string> Tags { get; set; } = new();

    // Custom properties
    public string OrderId { get; set; }
    public decimal Amount { get; set; }
    public string CustomerId { get; set; }
}
```

### 2. Register notification services

```csharp
using kdyf.Notifications.Integration;

// In your Program.cs or Startup.cs
var builder = services.AddKdyfNotification(configuration);

// InMemory transport is registered by default
// Optionally add Redis transport
// builder.AddRedisTarget(config => { /* Redis config */ });
// builder.AddRedisSource(config => { /* Redis config */ });

// Build the notification system
builder.Build();
```

### 3. Emit notifications

```csharp
using kdyf.Notifications.Interfaces;

public class OrderService
{
    private readonly INotificationEmitter _notificationEmitter;

    public OrderService(INotificationEmitter notificationEmitter)
    {
        _notificationEmitter = notificationEmitter;
    }

    public async Task CreateOrderAsync(Order order)
    {
        // Process order...

        // Emit notification (fire-and-forget)
        await _notificationEmitter.NotifyAsync(new OrderCreatedNotification
        {
            OrderId = order.Id,
            Amount = order.Total,
            CustomerId = order.CustomerId,
            Tags = new HashSet<string> { "orders", "created" }
        });
    }
}
```

### 4. Receive notifications

```csharp
using kdyf.Notifications.Interfaces;

public class OrderNotificationHandler : IHostedService
{
    private readonly INotificationReceiver _notificationReceiver;
    private IDisposable? _subscription;

    public OrderNotificationHandler(INotificationReceiver notificationReceiver)
    {
        _notificationReceiver = notificationReceiver;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Subscribe to specific notification type
        _subscription = _notificationReceiver
            .Receive<OrderCreatedNotification>(cancellationToken)
            .Subscribe(notification =>
            {
                Console.WriteLine($"Order created: {notification.OrderId}");
                Console.WriteLine($"Amount: {notification.Amount:C}");
            });

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _subscription?.Dispose();
        return Task.CompletedTask;
    }
}
```

## Tag-Based Filtering

Filter notifications by tags to receive only relevant notifications.

```csharp
// Emit with tags
await _notificationEmitter.NotifyAsync(new OrderCreatedNotification
{
    OrderId = "12345",
    Tags = new HashSet<string> { "orders", "high-priority", "vip-customer" }
});

// Receive only notifications with specific tags
_notificationReceiver
    .Receive<OrderCreatedNotification>(cancellationToken, "high-priority", "vip-customer")
    .Subscribe(notification =>
    {
        // Only receives notifications with BOTH tags
        Console.WriteLine($"High priority VIP order: {notification.OrderId}");
    });
```

## Receive All Notification Types

Subscribe to all notifications regardless of type.

```csharp
_notificationReceiver
    .Receive(cancellationToken) // No type parameter = all types
    .Subscribe(notification =>
    {
        Console.WriteLine($"Received: {notification.NotificationType}");
        Console.WriteLine($"ID: {notification.NotificationId}");
        Console.WriteLine($"Timestamp: {notification.Timestamp}");
    });
```

## Reactive Operators

Leverage System.Reactive operators for powerful stream processing.

```csharp
using System.Reactive.Linq;

_notificationReceiver
    .Receive<OrderCreatedNotification>(cancellationToken)
    .Where(n => n.Amount > 1000) // Filter high-value orders
    .Throttle(TimeSpan.FromSeconds(1)) // Rate limiting
    .Buffer(TimeSpan.FromSeconds(5)) // Batch notifications
    .Subscribe(batch =>
    {
        Console.WriteLine($"Received {batch.Count} high-value orders in 5 seconds");
        foreach (var notification in batch)
        {
            Console.WriteLine($"  - Order {notification.OrderId}: {notification.Amount:C}");
        }
    });
```

## Multiple Transport Configuration

The notification system supports multiple transports working together through a composite pattern.

```csharp
var builder = services.AddKdyfNotification(configuration);

// InMemory transport (always included by default)
// - Fast, in-process communication
// - No persistence
// - Single application instance only

// Optionally add additional transports (e.g., Redis via kdyf.Notifications.Redis)
// Additional transports enable:
// - Distributed scenarios
// - Persistence and durability
// - Cross-application communication
// - Horizontal scalability

builder.Build();
```

### How Multiple Transports Work

1. **Emitter (Composite Pattern)**:
   - Emits to ALL configured transports simultaneously
   - Each transport handles the notification independently
   - Fire-and-forget: `NotifyAsync` returns immediately

2. **Receiver (Composite Pattern with Deduplication)**:
   - Receives from ALL configured transports
   - Automatically deduplicates notifications by `NotificationId`
   - Ensures each notification is delivered exactly once to subscribers
   - Uses in-memory cache with LRU eviction

```csharp
// Example: Notification flow with multiple transports

// Emit notification
await emitter.NotifyAsync(notification);
// -> Goes to InMemory transport (local subscribers)
// -> Goes to additional transports (if configured)

// Receive notification
receiver.Receive<T>().Subscribe(...);
// -> Receives from InMemory (immediate, in-process)
// -> Receives from additional transports (if configured)
// -> Deduplication ensures subscriber gets it only once
```

## Notification Entity Interface

All notifications must implement `INotificationEntity`:

```csharp
using Microsoft.Extensions.Logging;

public interface INotificationEntity
{
    /// <summary>
    /// Unique identifier for this notification instance.
    /// Used for deduplication across transports.
    /// Can be a business ID or auto-generated GUID string.
    /// </summary>
    string NotificationId { get; set; }

    /// <summary>
    /// When the notification was created.
    /// </summary>
    DateTime Timestamp { get; set; }

    /// <summary>
    /// Type name of the notification for routing.
    /// Typically set to the class name.
    /// </summary>
    string NotificationType { get; set; }

    /// <summary>
    /// Optional group identifier for related notifications.
    /// </summary>
    string? GroupId { get; set; }

    /// <summary>
    /// Log level of the notification (Warn, Error, Info, Debug, etc.).
    /// </summary>
    LogLevel Level { get; set; }

    /// <summary>
    /// Message content of the notification.
    /// </summary>
    string Message { get; set; }

    /// <summary>
    /// Tags for filtering (uses HashSet for efficient lookups).
    /// </summary>
    HashSet<string> Tags { get; set; }
}
```

## Error Handling

Handle errors in notification processing gracefully.

```csharp
_notificationReceiver
    .Receive<OrderCreatedNotification>(cancellationToken)
    .Subscribe(
        onNext: notification =>
        {
            // Process notification
            ProcessOrder(notification);
        },
        onError: error =>
        {
            // Handle errors
            _logger.LogError(error, "Error processing notification");
        },
        onCompleted: () =>
        {
            // Stream completed (usually when cancelled)
            _logger.LogInformation("Notification stream completed");
        });
```

## Health Checks

The notification system includes built-in health checks.

```csharp
// Health checks are automatically registered
// Access via ASP.NET Core health check endpoints

// In Program.cs
app.MapHealthChecks("/health");

// InMemory transport: Always healthy (in-process)
// Additional transports may have their own health checks
```

## Best Practices

### 1. Always Set NotificationId

Ensure each notification has a unique ID for proper deduplication:

```csharp
public class MyNotification : INotificationEntity
{
    public string NotificationId { get; set; } = Guid.NewGuid().ToString();
    // ... other properties
}
```

### 2. Use Cancellation Tokens

Pass cancellation tokens to stop subscriptions cleanly:

```csharp
using var cts = new CancellationTokenSource();

var subscription = _notificationReceiver
    .Receive<MyNotification>(cts.Token)
    .Subscribe(notification => { /* ... */ });

// Later, when stopping
cts.Cancel();
subscription.Dispose();
```

### 3. Dispose Subscriptions

Always dispose subscriptions to prevent memory leaks:

```csharp
public class MyService : IDisposable
{
    private IDisposable? _subscription;

    public void Subscribe()
    {
        _subscription = _notificationReceiver
            .Receive<MyNotification>(CancellationToken.None)
            .Subscribe(notification => { /* ... */ });
    }

    public void Dispose()
    {
        _subscription?.Dispose();
    }
}
```

### 4. Choose the Right Transport

- **InMemory** (included by default):
  - Single application instance
  - No persistence needed
  - Highest performance
  - In-process communication only

- **Additional Transports** (via extension packages):
  - Multiple application instances
  - Persistence and durability
  - Cross-application communication
  - Horizontal scalability
  - See kdyf.Notifications.Redis for Redis Streams support

### 5. Use Tags for Filtering

Leverage tags to create flexible subscription patterns:

```csharp
// Emit with multiple tags
notification.Tags = new HashSet<string> { "domain:orders", "priority:high", "region:us-west" };

// Subscribe with specific tag combinations
receiver.Receive<T>(ct, "domain:orders", "priority:high");
```

## Performance Considerations

- **InMemory Transport**: Very fast, in-process communication with minimal overhead
- **Multiple Transports**: Each transport adds processing overhead; only add what you need
- **Deduplication**: Uses in-memory cache (LRU eviction after 10,000 entries or 1-hour TTL)
- **Reactive Operators**: Use throttling and batching to control message flow
- **Serialization**: Notifications may be serialized by some transports; keep payloads reasonable
- **HashSet for Tags**: Uses `HashSet<string>` for efficient tag lookups and filtering

## Thread Safety

✅ All notification emitters and receivers are thread-safe and can be safely shared across threads.

## Requirements

- .NET 8.0 or higher
- System.Reactive 6.1.0+
- Microsoft.Extensions.DependencyInjection.Abstractions 8.0+
- Microsoft.Extensions.Hosting.Abstractions 8.0+

## Related Packages

- **kdyf.Notifications.Redis**: Redis Streams transport provider

## License

This project is licensed under the MIT License - see the LICENSE file for details.

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## Support

For issues, questions, or contributions, please visit the [GitHub repository](https://github.com/kreadyf/kdyf.common).

---

Made with ❤️ by Kreadyf SRL
