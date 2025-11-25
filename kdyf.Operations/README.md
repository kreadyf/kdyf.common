# kdyf.Operations

[![NuGet](https://img.shields.io/nuget/v/kdyf.Operations.svg)](https://www.nuget.org/packages/kdyf.Operations/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

Lightweight operations orchestration framework for .NET that provides composable operation executors with sequential and concurrent (async pipeline) execution patterns.

## Features

- **Sequential Execution**: Execute operations one after another with `SequenceExecutor`
- **Concurrent Pipeline Execution**: Producer-consumer pattern with `AsyncPipelineExecutor`
- **Fluent API**: Build complex workflows with an intuitive, chainable API
- **Nested Operations**: Compose sequences and pipelines within other executors
- **Real-time Status Tracking**: Monitor execution status of all operations
- **Error Handling**: Comprehensive error capture with detailed error trees
- **Cancellation Support**: Full integration with `CancellationToken`
- **Dependency Injection**: Seamless integration with Microsoft.Extensions.DependencyInjection
- **Backpressure Control**: Configurable buffering in async pipelines
- **Type-safe**: Strongly-typed operations with compile-time checking

## Installation

```bash
dotnet add package kdyf.Operations
```

## Quick Start

### 1. Register the framework

```csharp
using kdyf.Operations.Integration;

// In your Program.cs or Startup.cs
services.AddKdyfOperations(typeof(Program).Assembly);
```

### 2. Define your operations

```csharp
using kdyf.Operations.Integration;

public class ValidateOrderOperation : IOperation<OrderData>
{
    public Task<OrderData> ExecuteAsync(OrderData input, CancellationToken cancellationToken)
    {
        // Validate order
        if (string.IsNullOrEmpty(input.CustomerId))
            throw new InvalidOperationException("Customer ID is required");

        input.IsValid = true;
        return Task.FromResult(input);
    }

    public event IOperation<OrderData>.StatusChangedEventHandler? OnStatusChanged;
}

public class CalculateTotalOperation : IOperation<OrderData>
{
    public Task<OrderData> ExecuteAsync(OrderData input, CancellationToken cancellationToken)
    {
        // Calculate total
        input.Total = input.Items.Sum(i => i.Price * i.Quantity);
        return Task.FromResult(input);
    }

    public event IOperation<OrderData>.StatusChangedEventHandler? OnStatusChanged;
}
```

### 3. Execute operations sequentially

```csharp
var executor = serviceProvider.CreateCommonOperationExecutor<OrderData>();

executor
    .Add<ValidateOrderOperation, OrderData>()
    .Add<CalculateTotalOperation, OrderData>()
    .Add<SaveOrderOperation, OrderData>();

var result = await executor.ExecuteAsync(orderData, cancellationToken);
```

## Sequential Execution

Operations execute one after another. The output of each operation becomes the input to the next.

```csharp
var executor = serviceProvider.CreateCommonOperationExecutor<MyData>();

executor
    .Add<OperationA, MyData>()
    .Add<OperationB, MyData>()
    .Add<OperationC, MyData>();

var result = await executor.ExecuteAsync(inputData, cancellationToken);
```

## Async Pipeline Execution

Execute operations concurrently with a producer-consumer pattern. Ideal for processing streams of data.

```csharp
public class DataProducerOperation : IAsyncProducerOperation<DataItem>
{
    public async IAsyncEnumerable<DataItem> ExecuteAsync(
        DataItem input,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (int i = 0; i < 100; i++)
        {
            yield return new DataItem { Id = i, Value = input.Value };
            await Task.Delay(10, cancellationToken);
        }
    }

    public event IAsyncProducerOperation<DataItem>.StatusChangedEventHandler? OnStatusChanged;
}

// Usage
var executor = serviceProvider.CreateCommonOperationExecutor<MyData>();

executor
    .Add<InitOperation, MyData>()
    .AddAsyncPipeline<DataItem>(
        input => new DataItem { Value = input.InitialValue },
        pipeline => pipeline
            .Add<DataProducerOperation, DataItem>()  // Producer
            .Add<ProcessItemOperation, DataItem>()    // Consumer 1
            .Add<TransformItemOperation, DataItem>(), // Consumer 2
        (pipelineOutput, outerInput) =>
        {
            outerInput.ProcessedCount = pipelineOutput.Id;
        })
    .Add<FinalOperation, MyData>();

var result = await executor.ExecuteAsync(myData, cancellationToken);
```

## Nested Sequences

Compose complex workflows by nesting sequences within other executors.

```csharp
var executor = serviceProvider.CreateCommonOperationExecutor<OrderData>();

executor
    .Add<ValidateOrderOperation, OrderData>()
    .AddSequence<PaymentData>(
        // Map input
        input => new PaymentData
        {
            Amount = input.Total,
            CustomerId = input.CustomerId
        },
        // Inner sequence
        innerExec => innerExec
            .Add<AuthorizePaymentOperation, PaymentData>()
            .Add<ChargePaymentOperation, PaymentData>()
            .Add<SendReceiptOperation, PaymentData>(),
        // Map output back
        (paymentOutput, orderInput) =>
        {
            orderInput.PaymentId = paymentOutput.TransactionId;
            orderInput.IsPaid = paymentOutput.Success;
        })
    .Add<FulfillOrderOperation, OrderData>();

var result = await executor.ExecuteAsync(orderData, cancellationToken);
```

## Conditional Execution

Skip operations based on conditions.

```csharp
executor
    .Add<OperationA, MyData>()
    .AddSequence<SubData>(
        condition: input => input.ShouldProcessSubData, // Only execute if true
        mapInto: input => new SubData { Value = input.Value },
        exec: innerExec => innerExec.Add<ProcessSubDataOperation, SubData>(),
        mapOut: (subOutput, mainInput) => mainInput.ProcessedValue = subOutput.Result)
    .Add<OperationB, MyData>();
```

## Execution Status Tracking

Monitor the execution status of all operations in real-time.

```csharp
executor.OnExecutionStatusChanged += (status) =>
{
    Console.WriteLine($"Operation: {status.Name}");
    Console.WriteLine($"Status: {status.Status}");
    Console.WriteLine($"Progress: {status.CompletionPercentage}%");
    Console.WriteLine($"Started: {status.Started}");
    Console.WriteLine($"Completed: {status.Completed}");

    if (status.Error != null)
    {
        Console.WriteLine($"Error: {status.Error.Message}");
    }
};

var result = await executor.ExecuteAsync(data, cancellationToken);
```

## Error Handling

Comprehensive error capture with detailed error trees.

```csharp
try
{
    var result = await executor.ExecuteAsync(data, cancellationToken);
}
catch (Exception ex)
{
    // Access detailed execution tree with error information
    var executionTree = executor.GetExecutionTree();

    foreach (var operation in executionTree.Operations.Values)
    {
        if (operation.Status == OperationState.Faulted)
        {
            Console.WriteLine($"Failed: {operation.Name}");
            Console.WriteLine($"Error: {operation.Error?.Message}");
            Console.WriteLine($"Stack: {operation.SerializableError?.StackTrace}");
        }
    }
}
```

## Cancellation Support

All operations support cancellation via `CancellationToken`.

```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

try
{
    var result = await executor.ExecuteAsync(data, cts.Token);
}
catch (OperationCanceledException)
{
    Console.WriteLine("Operation was cancelled");

    // Check which operations completed before cancellation
    foreach (var op in executor.Operations.Values)
    {
        if (op.Status == OperationState.Completed)
            Console.WriteLine($"Completed: {op.Name}");
        else if (op.Status == OperationState.Cancelled)
            Console.WriteLine($"Cancelled: {op.Name}");
    }
}
```

## Custom Scopes

Create custom dependency injection scopes for operations.

```csharp
executor.Add<MyOperation, MyData>(scopeFactory: sp =>
{
    // Create a custom scope with specific services
    var scope = sp.CreateScope();
    // Configure scope as needed
    return scope;
});
```

## Operation Attributes

Add metadata to operations using attributes.

```csharp
using kdyf.Operations.Extensions.Attributes;

[OperationDescriptor("Validate Order", "Validates order data and business rules")]
public class ValidateOrderOperation : IOperation<OrderData>
{
    // Implementation
}
```

## Advanced: AsyncPipelineExecutor Backpressure

The `AsyncPipelineExecutor` uses `BlockingCollection` with a default capacity of 2 items between stages. This provides:

- **Tight backpressure control**: Producer blocks quickly if consumer is slow
- **Low memory usage**: Only 2 items buffered per stage
- **Low latency**: Items don't queue for long

The pipeline stages run concurrently with automatic synchronization.

## Performance Considerations

- **SequenceExecutor**: Best for workflows where operations must run in strict order
- **AsyncPipelineExecutor**: Best for processing streams of data where operations can run concurrently
- **Nested Sequences**: Use sparingly; each level adds overhead
- **Status Events**: Subscribe to `OnExecutionStatusChanged` only when needed for monitoring
- **Operation Reuse**: Each executor instance should be used for a single execution; create new instances for concurrent executions

## Thread Safety

⚠️ **Important**: Executor instances are NOT thread-safe. Do NOT call `ExecuteAsync` concurrently on the same executor instance. Create separate executor instances for concurrent executions.

```csharp
// ❌ DON'T: Concurrent execution on same instance
var executor = serviceProvider.CreateCommonOperationExecutor<MyData>();
await Task.WhenAll(
    executor.ExecuteAsync(data1, ct),
    executor.ExecuteAsync(data2, ct) // WRONG!
);

// ✅ DO: Create separate instances
var executor1 = serviceProvider.CreateCommonOperationExecutor<MyData>();
var executor2 = serviceProvider.CreateCommonOperationExecutor<MyData>();
await Task.WhenAll(
    executor1.ExecuteAsync(data1, ct),
    executor2.ExecuteAsync(data2, ct) // Correct
);
```

## Requirements

- .NET 8.0 or higher
- Microsoft.Extensions.DependencyInjection.Abstractions 8.0.2+

## License

This project is licensed under the MIT License - see the LICENSE file for details.

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## Support

For issues, questions, or contributions, please visit the [GitHub repository](https://github.com/kreadyf/kdyf.common).

---

Made with ❤️ by Kreadyf SRL
