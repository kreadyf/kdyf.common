# kdyf.common

[![.NET](https://img.shields.io/badge/.NET-8.0-purple)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

A comprehensive collection of .NET libraries for building reactive, distributed, and composable applications. The kdyf.common solution provides essential infrastructure components for modern .NET applications.

## 📦 Packages

| Package | Version | Description |
|---------|---------|-------------|
| [kdyf.Notifications](./kdyf.Notifications) | [![NuGet](https://img.shields.io/nuget/v/kdyf.Notifications.svg)](https://www.nuget.org/packages/kdyf.Notifications/) | Reactive notification system with multi-transport support |
| [kdyf.Notifications.Redis](./kdyf.Notifications.Redis) | [![NuGet](https://img.shields.io/nuget/v/kdyf.Notifications.Redis.svg)](https://www.nuget.org/packages/kdyf.Notifications.Redis/) | Redis Streams transport provider for distributed notifications |
| [kdyf.Operations](./kdyf.Operations) | [![NuGet](https://img.shields.io/nuget/v/kdyf.Operations.svg)](https://www.nuget.org/packages/kdyf.Operations/) | Lightweight operations orchestration framework |

## 🚀 Overview

### kdyf.Notifications

A powerful reactive notification system built on System.Reactive, providing a composable infrastructure for application-wide event communication.

**Key Features:**
- Multi-transport support (InMemory, Redis Streams)
- Reactive observables with System.Reactive
- Composite pattern for coordinating multiple transports
- Tag-based filtering and type-safe routing
- Fire-and-forget async notification emission
- Built-in health checks and monitoring
- Cross-application communication support

[Learn more →](./kdyf.Notifications/README.md)

### kdyf.Notifications.Redis

Extends kdyf.Notifications with Redis Streams transport for distributed, persistent notification delivery across application boundaries.

**Key Features:**
- Redis Streams integration for reliable message delivery
- Fire-and-forget background processing with bounded channels
- Multiple storage strategies (Standard, Updateable, Stream-Only)
- Per-type stream routing and automatic trimming
- Configurable resilience and retry policies
- Horizontal scalability with consumer groups
- Health monitoring and observability

[Learn more →](./kdyf.Notifications.Redis/README.md)

### kdyf.Operations

A lightweight operations orchestration framework providing composable operation executors with sequential and concurrent execution patterns.

**Key Features:**
- Sequential and concurrent (async pipeline) execution
- Fluent API for building complex workflows
- Nested operations support
- Real-time status tracking
- Comprehensive error handling
- Full cancellation support
- Dependency injection integration
- Backpressure control in async pipelines

[Learn more →](./kdyf.Operations/README.md)

---

## Architecture

The solution is organized into three main packages:

| Package | Purpose |
|---------|---------|
| **kdyf.Notifications** | Core notification system with interfaces, base implementations, and in-memory transport |
| **kdyf.Notifications.Redis** | Redis Streams integration for distributed messaging |
| **kdyf.Operations** | Operations orchestration framework for complex workflows |

### Design Principles

- **Open/Closed Principle**: Extend functionality without modifying core code
- **Dependency Injection**: Fully integrated with Microsoft.Extensions.DependencyInjection
- **Separation of Concerns**: Clear boundaries between components
- **Thread Safety**: Safe for concurrent use across multiple threads
- **Reactive Programming**: Built on System.Reactive for powerful event-driven patterns

## 🎯 Use Cases

### Real-time Event Distribution
Use **kdyf.Notifications** and **kdyf.Notifications.Redis** to build event-driven architectures with real-time notification delivery across distributed systems.

```csharp
// Microservice A: Emit order events
await notificationEmitter.NotifyAsync(new OrderCreatedNotification
{
    OrderId = "12345",
    Amount = 99.99m
});

// Microservice B: Receive and process order events
notificationReceiver
    .Receive<OrderCreatedNotification>(cancellationToken)
    .Subscribe(notification => ProcessOrder(notification));
```

### Complex Workflow Orchestration
Use **kdyf.Operations** to orchestrate complex business workflows with sequential and concurrent operations.

```csharp
var executor = serviceProvider.CreateCommonOperationExecutor<OrderData>();

executor
    .Add<ValidateOrderOperation, OrderData>()
    .AddSequence<PaymentData>(
        input => new PaymentData { Amount = input.Total },
        innerExec => innerExec
            .Add<AuthorizePaymentOperation, PaymentData>()
            .Add<ChargePaymentOperation, PaymentData>(),
        (paymentOutput, orderInput) => orderInput.PaymentId = paymentOutput.TransactionId)
    .Add<FulfillOrderOperation, OrderData>();

var result = await executor.ExecuteAsync(orderData, cancellationToken);
```

### High-Throughput Data Processing
Combine **kdyf.Operations** async pipelines with **kdyf.Notifications.Redis** for high-throughput data processing pipelines.

```csharp
executor
    .AddAsyncPipeline<DataItem>(
        input => new DataItem { Value = input.InitialValue },
        pipeline => pipeline
            .Add<DataProducerOperation, DataItem>()
            .Add<ProcessItemOperation, DataItem>()
            .Add<NotifyCompletionOperation, DataItem>());
```

---

## 🚀 Getting Started

### Installation

Install the packages you need via NuGet:

```bash
# Core notification system
dotnet add package kdyf.Notifications

# Redis transport (optional)
dotnet add package kdyf.Notifications.Redis

# Operations orchestration
dotnet add package kdyf.Operations
```

### Basic Setup

```csharp
using kdyf.Notifications.Integration;
using kdyf.Notifications.Redis.Integration;
using kdyf.Operations.Integration;

var builder = WebApplication.CreateBuilder(args);

// Register notification system
var notificationBuilder = builder.Services.AddKdyfNotification(builder.Configuration);

// Add Redis transport (optional)
notificationBuilder.AddRedisTarget(config => config
    .WithStream("notifications:stream:default"));
notificationBuilder.AddRedisSource(config => config
    .WithStreams("notifications:stream:default"));

notificationBuilder.Build();

// Register operations framework
builder.Services.AddKdyfOperations(typeof(Program).Assembly);

var app = builder.Build();

// Add health checks
app.MapHealthChecks("/health");

app.Run();
```

### Configuration (appsettings.json)

```json
{
  "Redis": {
    "ConnectionString": "localhost:6379",
    "ConsumerGroup": "G_api_worker",
    "MaxConcurrentProcessing": 10,
    "Storage": {
      "DefaultStreamName": "notifications:stream:default",
      "MessageTTL": "01:00:00",
      "StreamTTL": "24:00:00",
      "MaxStreamLength": 10000
    },
    "Performance": {
      "ChannelCapacity": 10000,
      "ChannelFullMode": "Wait",
      "XReadGroupBlockMs": 5000
    }
  }
}
```

---

## 📖 Quick Examples

### Notifications: Emit and Receive

```csharp
// Define notification
public class OrderCreatedNotification : INotificationEntity
{
    public string NotificationId { get; set; } = Guid.NewGuid().ToString();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string NotificationType { get; set; } = nameof(OrderCreatedNotification);
    public string? GroupId { get; set; }
    public LogLevel Level { get; set; } = LogLevel.Information;
    public string Message { get; set; } = "Order created";
    public HashSet<string> Tags { get; set; } = new();

    public string OrderId { get; set; }
    public decimal Amount { get; set; }
}

// Emit notification
await notificationEmitter.NotifyAsync(new OrderCreatedNotification
{
    OrderId = "12345",
    Amount = 99.99m,
    Tags = new HashSet<string> { "orders", "created" }
});

// Receive notification
notificationReceiver
    .Receive<OrderCreatedNotification>(cancellationToken)
    .Subscribe(notification =>
    {
        Console.WriteLine($"Order {notification.OrderId} created!");
    });
```

### Operations: Build Workflows

```csharp
// Define operations
public class ValidateOrderOperation : IOperation<OrderData>
{
    public Task<OrderData> ExecuteAsync(OrderData input, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(input.CustomerId))
            throw new InvalidOperationException("Customer ID is required");

        input.IsValid = true;
        return Task.FromResult(input);
    }

    public event IOperation<OrderData>.StatusChangedEventHandler? OnStatusChanged;
}

// Execute workflow
var executor = serviceProvider.CreateCommonOperationExecutor<OrderData>();

executor
    .Add<ValidateOrderOperation, OrderData>()
    .Add<CalculateTotalOperation, OrderData>()
    .Add<SaveOrderOperation, OrderData>();

var result = await executor.ExecuteAsync(orderData, cancellationToken);
```

### Reactive Operators

```csharp
// Filter, throttle, and batch notifications
notificationReceiver
    .Receive<OrderCreatedNotification>(cancellationToken)
    .Where(n => n.Amount > 1000)
    .Throttle(TimeSpan.FromSeconds(1))
    .Buffer(TimeSpan.FromSeconds(5))
    .Subscribe(batch =>
    {
        Console.WriteLine($"Received {batch.Count} high-value orders");
    });
```

---

## 🏗️ Architecture Patterns

### Reactive Programming
The notification system is built on **System.Reactive**, providing powerful stream processing:

```csharp
notificationReceiver
    .Receive<OrderCreatedNotification>(cancellationToken)
    .Where(n => n.Amount > 1000)
    .Throttle(TimeSpan.FromSeconds(1))
    .Buffer(TimeSpan.FromSeconds(5))
    .Subscribe(batch => ProcessBatch(batch));
```

### Composite Pattern
Multiple transports work together seamlessly with automatic deduplication:

```csharp
// Emitter sends to ALL transports (InMemory + Redis)
await emitter.NotifyAsync(notification);

// Receiver merges from ALL transports with deduplication
receiver.Receive<T>().Subscribe(...); // Gets notification exactly once
```

### Producer-Consumer Pipeline
Async pipelines provide concurrent execution with backpressure control:

```csharp
pipeline
    .Add<ProducerOperation, DataItem>()   // Produces items
    .Add<ConsumerOperation1, DataItem>()  // Consumes concurrently
    .Add<ConsumerOperation2, DataItem>(); // Consumes concurrently
```

### Distributed Messaging Flow

```
Application A                Redis Streams               Application B
┌───────────┐              ┌──────────────┐            ┌───────────┐
│  Emitter  │─────Emit────>│   Stream     │<───Read───│  Worker   │
└───────────┘              │  Consumer    │            └─────┬─────┘
      │                    │   Groups     │                  │
      ↓                    └──────────────┘                  ↓
┌───────────┐                                          ┌───────────┐
│ InMemory  │                                          │ Re-emit   │
│ Receiver  │                                          │ to Local  │
└───────────┘                                          └───────────┘
```

**Key Benefits:**
- **Local-first**: In-memory delivery for same-process subscribers
- **Distributed**: Redis Streams for cross-application communication
- **Reliable**: Consumer groups ensure at-least-once delivery
- **Deduplication**: Automatic prevention of duplicate processing

---

## 📖 Documentation

Each package includes comprehensive documentation with examples and best practices:

- [kdyf.Notifications Documentation](./kdyf.Notifications/README.md)
- [kdyf.Notifications.Redis Documentation](./kdyf.Notifications.Redis/README.md)
- [kdyf.Operations Documentation](./kdyf.Operations/README.md)

---

## 📊 Performance

- **kdyf.Notifications InMemory**: Very fast in-process communication with minimal overhead
- **kdyf.Notifications.Redis**: Optimized for high-throughput with fire-and-forget background processing
- **kdyf.Operations**: Lightweight orchestration with minimal allocation overhead
- **Thread Safety**: All components are thread-safe and can be safely shared across threads
- **Deduplication**: Uses in-memory cache (LRU eviction after 10,000 entries or 1-hour TTL)
- **Backpressure**: Bounded channels with configurable capacity for flow control

---

## 🛠️ Requirements

- **.NET 8.0 or higher**
- Microsoft.Extensions.DependencyInjection.Abstractions 8.0+
- Microsoft.Extensions.Hosting.Abstractions 8.0+ (for kdyf.Notifications)
- System.Reactive 6.1.0+ (for kdyf.Notifications)
- StackExchange.Redis 2.9.32+ (for kdyf.Notifications.Redis)
- Redis Server 5.0+ (for kdyf.Notifications.Redis)

---

## 🧪 Testing

Each package includes comprehensive test suites:

- **Unit tests** for core functionality
- **Integration tests** for distributed scenarios
- **Performance tests** for high-throughput scenarios
- **End-to-end tests** for composite workflows

```bash
# Run all tests
dotnet test

# Run specific project tests
dotnet test kdyf.Notifications.Test/kdyf.Notifications.Test.csproj
dotnet test kdyf.Notifications.Test.Redis/kdyf.Notifications.Test.Redis.csproj
```

---

## 🔧 Configuration Reference

### Notification System

```json
{
  "Redis": {
    "ConnectionString": "localhost:6379",
    "ConsumerGroup": "G_api_worker",
    "MaxConcurrentProcessing": 10,
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

See individual package documentation for detailed configuration options.

---

## 🚦 Roadmap

### Planned Features
- [ ] Dapr integration
- [ ] Azure Service Bus transport
- [ ] RabbitMQ transport
- [ ] Enhanced metrics and observability
- [ ] Dead letter queue support
- [ ] Advanced persistence options
- [ ] Circuit breaker patterns

---

## 🤝 Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

### Development Setup

```bash
# Clone the repository
git clone https://github.com/kreadyf/kdyf.common.git

# Restore dependencies
dotnet restore

# Build the solution
dotnet build

# Run tests
dotnet test
```

### Contribution Guidelines

- Follow existing code style and conventions
- Add unit tests for new features
- Update documentation as needed
- Ensure all tests pass before submitting PR

---

## 📝 License

This project is licensed under the MIT License - see the [LICENSE](./LICENSE) file for details.

---

## 💬 Support

For issues, questions, or contributions:

- **GitHub Issues**: [https://github.com/kreadyf/kdyf.common/issues](https://github.com/kreadyf/kdyf.common/issues)
- **Documentation**: See individual package README files
- **Samples**: Check the sample projects in the solution:  
  - `kdyf.Notifications.Sample01.Console`
  - `kdyf.Notifications.Sample02.Console`

---

## 🎉 Acknowledgments

Built with modern .NET best practices and inspired by:

- **[System.Reactive](https://github.com/dotnet/reactive)** for reactive programming patterns
- **[StackExchange.Redis](https://github.com/StackExchange/StackExchange.Redis)** for Redis Streams integration
- **Microsoft.Extensions** patterns for dependency injection and configuration
- **Clean Architecture** principles for maintainable design

---

**Made with ❤️ by Kreadyf SRL**
