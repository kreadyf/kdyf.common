# kdyf.Notifications.Redis

[![NuGet](https://img.shields.io/nuget/v/kdyf.Notifications.Redis.svg)](https://www.nuget.org/packages/kdyf.Notifications.Redis/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

Redis Streams transport provider for kdyf.Notifications system. Enables distributed, persistent notification delivery with fire-and-forget background processing.

## Features

- **Redis Streams**: Leverage Redis Streams for reliable message delivery
- **Fire-and-Forget**: Background processing with bounded channels for backpressure
- **Multiple Storage Strategies**:
  - **Standard**: Key-value + Stream (updateable, queryable)
  - **Updateable**: Key-value with last-write-wins semantics
  - **Stream-Only**: Stream-only for fire-and-forget scenarios
- **Per-Type Stream Routing**: Route different notification types to different streams
- **Automatic Trimming**: Configure max stream length and TTL per stream
- **Resilience**: Configurable retry policies for transient failures
- **Health Checks**: Monitor Redis connection status
- **Horizontal Scalability**: Multiple instances can share the same Redis infrastructure
- **Cross-Application Communication**: Share notifications across application boundaries

## Installation

```bash
# Install the base package
dotnet add package kdyf.Notifications

# Install the Redis transport
dotnet add package kdyf.Notifications.Redis
```

## Quick Start

### 1. Add Redis connection string to configuration

```json
{
  "Redis": {
    "ConnectionString": "localhost:6379",
    "ConsumerGroup": "G_api_worker",
    "MaxConcurrentProcessing": "10",
    "Storage": {
      "DefaultStreamName": "notifications:stream:default",
      "MessageTTL": "01:00:00",
      "StreamTTL": "24:00:00",
      "MaxStreamLength": 10000,
      "UseApproximateTrimming": false
    },
    "Performance": {
      "ChannelCapacity": 10000,
      "ChannelFullMode": "Wait",
      "InitializationTimeoutMs": 30000,
      "XReadGroupBlockMs": 5000
    },
    "Resilience": {
      "RetryDelayMs": 2000,
      "ErrorRecoveryDelayMs": 1000
    }
  }
}
```

### 2. Register Redis transport

```csharp
using kdyf.Notifications.Integration;
using kdyf.Notifications.Redis.Integration;

var builder = services.AddKdyfNotification(configuration);

// Add Redis emitter (target)
builder.AddRedisTarget(config => config
    .WithStream("notifications:stream:default")); // Default stream for all types

// Add Redis receiver (source)
builder.AddRedisSource(config => config
    .WithStreams("notifications:stream:default"));

builder.Build();
```

### 3. Emit and receive notifications

```csharp
// Emit (same API as base package)
await _notificationEmitter.NotifyAsync(new OrderCreatedNotification
{
    OrderId = "12345",
    Amount = 99.99m
});

// Receive (same API as base package)
_notificationReceiver
    .Receive<OrderCreatedNotification>(cancellationToken)
    .Subscribe(notification =>
    {
        Console.WriteLine($"Order: {notification.OrderId}");
    });
```

## Storage Strategies

### Standard Strategy (Default)

Stores both in key-value (for queries) and stream (for pub-sub).

```csharp
builder.AddRedisTarget(config => config
    .WithStream("notifications:stream:orders"));

// Stored as:
// 1. Key: notifications:{NotificationId} -> JSON value (with TTL)
// 2. Stream: notifications:stream:orders -> Stream entry with metadata
```

**Use when**: You need to query notifications by ID AND subscribe to the stream.

### Updateable Strategy

Key-value storage with last-write-wins semantics.

```csharp
builder.AddRedisTarget(config => config
    .WithStream<OrderStatusNotification>("notifications:stream:orders"))
    .ConfigureUpdateable<OrderStatusNotification>(notif => notif.OrderId); // Use OrderId as key

// Stored as:
// 1. Key: notifications:status:{OrderId} -> Latest status (with TTL)
// 2. Stream: notifications:stream:orders -> All status updates
```

**Use when**: You want to track the latest state (e.g., order status, user presence).

```csharp
// Example: Track latest order status
public class OrderStatusNotification : INotificationEntity
{
    public string NotificationId { get; set; } = Guid.NewGuid().ToString();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string NotificationType { get; set; } = nameof(OrderStatusNotification);
    public string? GroupId { get; set; }
    public LogLevel Level { get; set; } = LogLevel.Information;
    public string Message { get; set; } = "Order status updated";
    public HashSet<string> Tags { get; set; } = new();

    // Custom properties
    public string OrderId { get; set; } // Used as updateable key
    public string Status { get; set; } // "pending", "shipped", "delivered"
}

// Configure
builder.AddRedisTarget(config => config
    .WithStream<OrderStatusNotification>("notifications:stream:orders"))
    .ConfigureUpdateable<OrderStatusNotification>(notif => notif.OrderId);

// Query latest status
var latestStatus = await redis.StringGetAsync("notifications:status:order-123");
```

### Stream-Only Strategy

Only stores in stream, skips key-value storage.

```csharp
builder.AddRedisTarget(config => config
    .WithStreamOnly<MetricsNotification>("notifications:stream:metrics"));

// Stored as:
// Stream: notifications:stream:metrics -> Stream entry only
// No key-value storage
```

**Use when**: Fire-and-forget notifications that don't need querying (metrics, logs, events).

## Per-Type Stream Routing

Route different notification types to different streams.

```csharp
builder.AddRedisTarget(config => config
    .WithStream("notifications:stream:default") // Default for unmapped types
    .WithStream<OrderCreatedNotification>("notifications:stream:orders")
    .WithStream<PaymentProcessedNotification>("notifications:stream:payments")
    .WithStreamOnly<MetricsNotification>("notifications:stream:metrics"));

// OrderCreatedNotification     -> notifications:stream:orders
// PaymentProcessedNotification -> notifications:stream:payments
// MetricsNotification          -> notifications:stream:metrics (stream-only)
// OtherNotification            -> notifications:stream:default (fallback)
```

## Configuration Options

### Storage Configuration

```json
{
  "Redis": {
    "Storage": {
      // Default stream name for unmapped types
      "DefaultStreamName": "notifications:stream:default",

      // TTL for key-value storage (individual messages)
      "MessageTTL": "01:00:00", // 1 hour

      // TTL for streams (entire stream)
      "StreamTTL": "24:00:00", // 24 hours

      // Max stream length (automatic trimming)
      "MaxStreamLength": 10000,

      // Use approximate trimming (~) for better performance
      "UseApproximateTrimming": false
    }
  }
}
```

### Performance Configuration

```json
{
  "Redis": {
    "Performance": {
      // Bounded channel capacity (backpressure threshold)
      // Default: 10000
      "ChannelCapacity": 10000,

      // Behavior when channel is full: Wait, DropNewest, DropOldest
      // Default: Wait (safest, prevents data loss)
      "ChannelFullMode": "Wait",

      // Timeout for Redis initialization operations (ms)
      // Default: 30000 (30 seconds)
      "InitializationTimeoutMs": 30000,

      // Duration XREADGROUP blocks waiting for new messages (ms)
      // Default: 5000 (5 seconds)
      "XReadGroupBlockMs": 5000
    }
  }
}
```

### Resilience Configuration

```json
{
  "Redis": {
    "Resilience": {
      // Delay before retrying failed Redis key-value operations (ms)
      // Default: 2000 (2 seconds)
      "RetryDelayMs": 2000,

      // Delay before retrying after Redis connection error in receiver (ms)
      // Default: 1000 (1 second)
      "ErrorRecoveryDelayMs": 1000
    }
  }
}
```

## Advanced Configuration

### Multiple Consumer Groups

Configure consumer groups via appsettings.json for different processing pipelines.

```json
{
  "Redis": {
    "ConnectionString": "localhost:6379",
    "ConsumerGroup": "order-processor-group",  // Consumer group name
    "MaxConcurrentProcessing": "10"
  }
}
```

```csharp
// Application A: Order processing (uses ConsumerGroup from config)
builder.AddRedisSource(config => config
    .WithStreams("notifications:stream:orders"));

// Application B: Analytics (configure different ConsumerGroup in its appsettings.json)
builder.AddRedisSource(config => config
    .WithStreams("notifications:stream:orders"));

// Both groups receive all messages independently
// Default consumer group name: "G_api_worker"
```

### Per-Type TTL Configuration

Configure different TTLs for different notification types.

```csharp
// In configuration
{
  "Redis": {
    "Storage": {
      "MessageTTL": "01:00:00", // Default: 1 hour
      "PerTypeTTL": {
        "OrderCreatedNotification": "24:00:00",    // 24 hours
        "MetricsNotification": "00:05:00",         // 5 minutes
        "AuditLogNotification": "30.00:00:00"      // 30 days
      }
    }
  }
}
```

### Stream Trimming Strategies

Control how streams are trimmed to manage memory usage.

```csharp
// Exact trimming (MAXLEN)
{
  "MaxStreamLength": 10000,
  "UseApproximateTrimming": false
}

// Approximate trimming (MAXLEN ~)
// Better performance, may keep slightly more than max
{
  "MaxStreamLength": 10000,
  "UseApproximateTrimming": true
}

// Disable trimming
{
  "MaxStreamLength": 0
}
```

## Background Processing

The Redis emitter uses fire-and-forget background processing with bounded channels.

```
┌─────────────┐
│ Your Code   │
│ NotifyAsync │ ← Returns immediately
└──────┬──────┘
       │
       ▼
┌─────────────────┐
│ Bounded Channel │ ← Capacity: 100 (configurable)
│ (Backpressure)  │
└──────┬──────────┘
       │
       ▼
┌─────────────────┐
│ Background      │
│ Processor       │ ← Batching + Retry
│ (HostedService) │
└──────┬──────────┘
       │
       ▼
┌─────────────────┐
│ Redis Streams   │
└─────────────────┘
```

**Backpressure**: If the channel is full, `NotifyAsync` will block until space is available.

## Health Checks

Redis transport includes automatic health monitoring.

```csharp
// In Program.cs
app.MapHealthChecks("/health");

// Health check monitors:
// - Redis connection status
// - Ping latency
// - Background processor status
```

Access health status:

```bash
curl http://localhost:5000/health

# Response
{
  "status": "Healthy",
  "results": {
    "redis_notification_emitter": {
      "status": "Healthy",
      "description": "Redis connection is healthy"
    }
  }
}
```

## Cross-Application Communication

Redis transport enables notifications across application boundaries.

### Application A (Producer)

```csharp
// Emitter only
var builder = services.AddKdyfNotification(configuration);
builder.AddRedisTarget(config => config
    .WithStream("notifications:stream:orders"));
builder.Build();

// Emit notifications
await _notificationEmitter.NotifyAsync(new OrderCreatedNotification
{
    OrderId = "12345"
});
```

### Application B (Consumer)

```csharp
// Receiver only
var builder = services.AddKdyfNotification(configuration);
builder.AddRedisSource(config => config
    .WithStreams("notifications:stream:orders"));
builder.Build();

// Receive notifications
_notificationReceiver
    .Receive<OrderCreatedNotification>(cancellationToken)
    .Subscribe(notification =>
    {
        Console.WriteLine($"App B received: {notification.OrderId}");
    });
```

## Monitoring and Observability

### Logging

The Redis transport logs important events:

```csharp
// Configure logging in appsettings.json
{
  "Logging": {
    "LogLevel": {
      "kdyf.Notifications.Redis": "Information"
    }
  }
}

// Log events include:
// - Connection established/lost
// - Retry attempts
// - Stream creation
// - Batch processing metrics
// - Errors and warnings
```

### Metrics

Key metrics to monitor:

- **Channel Utilization**: `ChannelCount / ChannelCapacity`
- **Processing Rate**: Messages per second
- **Retry Rate**: Retry attempts per second
- **Stream Length**: Current stream size
- **Latency**: Time from emit to receive

## Error Handling

### Transient Failures

The Redis transport automatically retries transient failures.

```json
{
  "Redis": {
    "Resilience": {
      "RetryDelayMs": 2000,
      "ErrorRecoveryDelayMs": 1000
    }
  }
}

// Retries automatically on:
// - Connection timeout (uses ErrorRecoveryDelayMs)
// - Network errors (uses ErrorRecoveryDelayMs)
// - Redis key-value operation failures (uses RetryDelayMs)
```

### Permanent Failures

```csharp
// Monitor health checks for permanent failures
app.MapHealthChecks("/health");

// Implement circuit breaker pattern if needed
// Consider falling back to InMemory transport
```

## Performance Tuning

### High Throughput

```json
{
  "Redis": {
    "Performance": {
      "ChannelCapacity": 100000,        // Large buffer for bursts
      "ChannelFullMode": "Wait",        // Prevent data loss
      "XReadGroupBlockMs": 10000        // Less frequent polling
    }
  }
}
```

### Low Latency

```json
{
  "Redis": {
    "Performance": {
      "ChannelCapacity": 1000,          // Small buffer for fast processing
      "ChannelFullMode": "Wait",        // Prevent data loss
      "XReadGroupBlockMs": 1000         // More responsive (1 second)
    },
    "Resilience": {
      "ErrorRecoveryDelayMs": 500       // Fast recovery
    }
  }
}
```

### Memory Optimization

```json
{
  "Redis": {
    "Storage": {
      "MaxStreamLength": 1000,          // Aggressive trimming
      "UseApproximateTrimming": true,   // Better performance
      "MessageTTL": "00:05:00",         // Short TTL (5 min)
      "StreamTTL": "01:00:00"           // Short stream TTL (1 hour)
    },
    "Performance": {
      "ChannelCapacity": 1000           // Smaller memory footprint
    }
  }
}

// Use stream-only for high-volume notifications
builder.AddRedisTarget(config => config
    .WithStreamOnly<HighVolumeNotification>("notifications:stream:metrics"));
```

## Best Practices

### 1. Choose the Right Storage Strategy

```csharp
// Standard: Need to query + subscribe
.WithStream<OrderCreatedNotification>("notifications:stream:orders")

// Updateable: Track latest state
.AsUpdateable<OrderStatusNotification>(n => n.OrderId)

// Stream-Only: Fire-and-forget, high volume
.WithStreamOnly<MetricsNotification>("notifications:stream:metrics")
```

### 2. Use Consumer Groups for Multiple Instances

```csharp
// Ensures each message is processed by only one instance
// Configure consumer group in appsettings.json
builder.AddRedisSource(config => config
    .WithStreams("notifications:stream:orders"));
```

```json
{
  "Redis": {
    "ConsumerGroup": "order-processor-v1"
  }
}
```

### 3. Monitor Channel Capacity

```json
// If channel is frequently full, increase capacity or adjust behavior
{
  "Performance": {
    "ChannelCapacity": 100000,        // Increase if blocking occurs
    "ChannelFullMode": "Wait",        // Or use DropNewest for non-critical data
    "XReadGroupBlockMs": 5000
  }
}
```

### 4. Set Appropriate TTLs

```json
// Balance between data retention and memory usage
{
  "Storage": {
    "MessageTTL": "01:00:00",       // Query window (1 hour)
    "StreamTTL": "24:00:00",        // Replay window (24 hours)
    "MaxStreamLength": 10000        // Memory limit
  }
}
```

### 5. Use Stream Routing for Separation

```csharp
// Separate high-volume from critical notifications
builder.AddRedisTarget(config => config
    .WithStream<CriticalAlert>("notifications:stream:alerts")
    .WithStreamOnly<MetricsEvent>("notifications:stream:metrics"));
```

## Troubleshooting

### High Memory Usage

- Reduce `MaxStreamLength`
- Enable `UseApproximateTrimming`
- Decrease `MessageTTL` and `StreamTTL`
- Use stream-only strategy for high-volume types

### Messages Not Received

- Check consumer group configuration
- Verify stream names match between emitter and receiver
- Check Redis connection health
- Review logs for errors

### Slow Performance

- Increase `BatchSize`
- Decrease `BatchDelay`
- Use `UseApproximateTrimming`
- Check Redis server performance

### Backpressure Issues

- Increase `ChannelCapacity`
- Increase `BatchSize` and `BatchDelay`
- Scale horizontally (add more instances)
- Consider stream-only strategy

## Requirements

- .NET 8.0 or higher
- kdyf.Notifications 1.0.0+
- StackExchange.Redis 2.9.32+
- Redis Server 5.0+ (for Redis Streams support)

## Related Packages

- **kdyf.Notifications**: Base notification system (required)

## License

This project is licensed under the MIT License - see the LICENSE file for details.

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## Support

For issues, questions, or contributions, please visit the [GitHub repository](https://github.com/kreadyf/kdyf.common).

---

Made with ❤️ by Kreadyf SRL
