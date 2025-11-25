using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using kdyf.Operations.Extensions;
using kdyf.Operations.Extensions.Attributes;
using kdyf.Operations.Integration;

namespace kdyf.Operations.Executors;

/// <summary>
/// Executor that runs operations concurrently as a pipeline with producer-consumer pattern.
/// </summary>
/// <typeparam name="TExecutorInputOutput">The type of input and output data processed by the executor.</typeparam>
/// <remarks>
/// <para><b>Execution Model:</b></para>
/// <para>The first operation is a producer that yields items asynchronously. Each subsequent operation
/// consumes items from the previous stage and produces items for the next stage. All stages run concurrently.</para>
/// <para><b>Internal Concurrency:</b></para>
/// <list type="bullet">
///   <item><description>Each operation runs in its own Task.Run() task.</description></item>
///   <item><description>Operations are connected by bounded BlockingCollection queues (capacity: <see cref="DefaultPipelineBufferCapacity"/>).</description></item>
///   <item><description>Backpressure: When a queue is full, the producer blocks until the consumer processes items.</description></item>
/// </list>
/// <para><b>Thread Safety:</b></para>
/// <list type="bullet">
///   <item><description>Do NOT call <see cref="ExecuteAsync"/> concurrently on the same instance.</description></item>
///   <item><description>Internal concurrent access to <see cref="BaseExecutor{TExecutorInputOutput}.Operations"/> is safe because
///   each concurrent task accesses a different key (operation ID).</description></item>
/// </list>
/// <para><b>Cancellation:</b></para>
/// <para>Supports cancellation via <see cref="CancellationToken"/>. When cancelled, all pipeline stages
/// stop processing and the executor cleans up resources.</para>
/// </remarks>
[OperationDescriptor("Async Pipeline Executor", "Description Async Pipeline Executor")]
public class AsyncPipelineExecutor<TExecutorInputOutput> : BaseExecutor<TExecutorInputOutput>,
    IAsyncPipelineStartExecutor<TExecutorInputOutput>, IAsyncPipelineExecutor<TExecutorInputOutput>
{
    /// <summary>
    /// Default pipeline buffer capacity between stages.
    /// Controls how many items can be buffered between producer and consumer stages.
    ///
    /// Current value (2) provides:
    /// - Tight backpressure control (producer blocks quickly if consumer is slow)
    /// - Low memory usage (only 2 items buffered per stage)
    /// - Low latency (items don't queue for long)
    ///
    /// Trade-offs:
    /// - Lower values: Tighter backpressure, less memory, may reduce throughput if processing time varies
    /// - Higher values: Better throughput for variable workloads, more memory, delayed backpressure
    /// </summary>
    private const int DefaultPipelineBufferCapacity = 2;

    /// <summary>
    /// Initializes a new instance of the <see cref="AsyncPipelineExecutor{TExecutorInputOutput}"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider for dependency injection.</param>
    public AsyncPipelineExecutor(IServiceProvider serviceProvider) : base(serviceProvider)
    {
    }

    /// <summary>
    /// Adds a producer operation to the pipeline. This must be the first operation added.
    /// </summary>
    /// <typeparam name="TOperation">The type of producer operation.</typeparam>
    /// <typeparam name="TInputOutput">The type of data produced by the operation.</typeparam>
    /// <param name="scopeFactory">Optional factory to create a custom DI scope for the operation.</param>
    /// <returns>The executor instance for fluent chaining.</returns>
    public new IAsyncPipelineExecutor<TExecutorInputOutput> Add<TOperation, TInputOutput>(Func<IServiceProvider, IServiceScope>? scopeFactory = null)
        where TOperation : IAsyncProducerOperation<TInputOutput>
    {
        var sp = ResolveServiceProvider(scopeFactory);

        var id = Guid.NewGuid();

        var friendlyNameAndDescription = typeof(TOperation).GetFriendlyTypeNameAndDescription();
        Operations[id] = new ExecutionStatus(sp.GetRequiredService<TOperation>(), id, friendlyNameAndDescription.Item1, friendlyNameAndDescription.Item2);

        return this;
    }

    /// <summary>
    /// Executes all configured operations as a concurrent pipeline with the provided input.
    /// </summary>
    /// <param name="input">The input data to be processed by the producer operation.</param>
    /// <param name="cancellationToken">The cancellation token to cancel the execution.</param>
    /// <returns>A task representing the asynchronous operation, with the final processed output from the last consumer.</returns>
    /// <remarks>
    /// <para><b>Execution Flow:</b></para>
    /// <list type="number">
    ///   <item><description>The producer operation yields items asynchronously from the input.</description></item>
    ///   <item><description>Items flow through consumer operations connected by BlockingCollection queues.</description></item>
    ///   <item><description>All operations run concurrently in separate tasks.</description></item>
    ///   <item><description>The final output comes from the last operation in the pipeline.</description></item>
    /// </list>
    /// <para>If any operation throws an exception, all pipeline stages are cancelled and the exception is propagated.</para>
    /// </remarks>
    public override async Task<TExecutorInputOutput> ExecuteAsync(TExecutorInputOutput input, CancellationToken cancellationToken)
    {
        List<(ExecutionStatus OperationItem, BlockingCollection<TExecutorInputOutput> Source)> consumerProducerOperations
            = Operations.Values.Skip(1).Select(s => (s, new BlockingCollection<TExecutorInputOutput>(DefaultPipelineBufferCapacity))).ToList();

        TExecutorInputOutput? result = default;
        List<Task> operationTasks = new();

        // Create linked cancellation token source and synchronization task
        // This ensures proper cancellation propagation throughout the pipeline
        using CancellationTokenSource linkedCts = new CancellationTokenSource();
        var ctsTask = linkedCts.CreateLinkedCancellationTokenSource(cancellationToken);

        try
        {


            foreach (var (operation, i) in consumerProducerOperations.Select((value, i) => (value, i)))
            {
                var operationId = operation.OperationItem.Id;
                operationTasks.Add(
                    Task.Run(async () =>
                    {
                        BlockingCollection<TExecutorInputOutput>? target = i + 1 < consumerProducerOperations.Count ? consumerProducerOperations[i + 1].Source : null;

                        UpdateOperationItem(Operations[operationId] with { Started = DateTime.UtcNow, Status = OperationState.Running });

                        try
                        {
                            foreach (var itm in consumerProducerOperations[i].Source.GetConsumingEnumerable(linkedCts.Token)) // GetConsumingEnumerable does never throw OperationCanceledException, so manual ending the completion
                            {
                                linkedCts.Token.ThrowIfCancellationRequested();

                                var res = await HandleOperation(Operations[operationId], itm, linkedCts.Token);
                                if (target != null)
                                    target.Add(res, linkedCts.Token);
                                else
                                    // Safe: Only the last consumer (target == null) writes to result
                                    // No race condition since only one task has target == null
                                    result = res;
                            }

                            target?.CompleteAdding();

                            linkedCts.Token.ThrowIfCancellationRequested();

                            UpdateOperationItem(Operations[operationId] with { Completed = DateTime.UtcNow, Status = OperationState.Completed });
                        }
                        catch (Exception ex) when (ex is OperationCanceledException or TaskCanceledException)
                        {
                            UpdateOperationItem(Operations[operationId] with { Status = OperationState.Cancelled });
                            throw;
                        }
                    }, linkedCts.Token)
                    .CancelOnException(linkedCts)
                ); ;
            }

            var initialBlockingCollection = consumerProducerOperations.First().Source;
            var operationItem = Operations.Values.First();
            var operationItemId = operationItem.Id;
            var asyncPipelineProducer = operationItem.Operation;

            var producerExecuteMethod = GetMethodByOperation<TExecutorInputOutput>(asyncPipelineProducer, operationItem.Id);

            try
            {
                UpdateOperationItem(Operations[operationItemId] with { Started = DateTime.UtcNow, Status = OperationState.Running });

                var asyncEnumerable = (IAsyncEnumerable<object>)producerExecuteMethod!.Invoke(asyncPipelineProducer, new object[] { input!, linkedCts.Token })!;

                await foreach (var item in asyncEnumerable!)
                {
                    initialBlockingCollection.Add((TExecutorInputOutput)item, linkedCts.Token);
                }

                UpdateOperationItem(Operations[operationItemId] with { Completed = DateTime.UtcNow, Status = OperationState.Completed });
            }
            catch (Exception ex) when (ex is OperationCanceledException or TaskCanceledException)
            {
                UpdateOperationItem(Operations[operationItemId] with { Status = OperationState.Cancelled });
                throw;
            }
            finally
            {
                initialBlockingCollection.CompleteAdding();
            }

            await Task.WhenAll(operationTasks);
        }
        finally
        {
            // Cancel all pipeline operations and wait for synchronization
            // The await ensures all operations observe cancellation before cleanup
            linkedCts.Cancel();
            await ctsTask;
        }

        return result!;
    }
}
