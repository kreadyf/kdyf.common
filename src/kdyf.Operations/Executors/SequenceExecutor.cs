using kdyf.Operations;
using kdyf.Operations.Executors;
using kdyf.Operations.Extensions.Attributes;
using kdyf.Operations.Integration;

namespace kdyf.Operations.Executors;

/// <summary>
/// Executor that runs operations sequentially, one after another.
/// </summary>
/// <typeparam name="TExecutorInputOutput">The type of input and output data processed by the executor.</typeparam>
/// <remarks>
/// <para><b>Execution Model:</b></para>
/// <para>Operations are executed one at a time in the order they were added.
/// The output of each operation becomes the input to the next operation.</para>
/// <para><b>Thread Safety:</b></para>
/// <para>This executor is NOT thread-safe. Do NOT call <see cref="ExecuteAsync"/> concurrently
/// on the same instance. Create separate instances for concurrent executions.</para>
/// <para><b>Cancellation:</b></para>
/// <para>Supports cancellation via <see cref="CancellationToken"/>. When cancelled, the current operation
/// completes, then the executor stops and marks remaining operations as not executed.</para>
/// </remarks>
[OperationDescriptor("Sequence Executor", "Description Sequence Executor")]
public class SequenceExecutor<TExecutorInputOutput> : BaseExecutor<TExecutorInputOutput>, ISequenceStartExecutor<TExecutorInputOutput>, ISequenceExecutor<TExecutorInputOutput>
{

    /// <summary>
    /// Initializes a new instance of the <see cref="SequenceExecutor{TExecutorInputOutput}"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider for dependency injection.</param>
    public SequenceExecutor(IServiceProvider serviceProvider) : base(serviceProvider)
    {
    }

    /// <summary>
    /// Executes all configured operations sequentially with the provided input.
    /// </summary>
    /// <param name="input">The input data to be processed by the operations.</param>
    /// <param name="cancellationToken">The cancellation token to cancel the execution.</param>
    /// <returns>A task representing the asynchronous operation, with the final processed output.</returns>
    /// <remarks>
    /// <para>Operations are executed in sequence. Each operation receives the output of the previous operation as its input.</para>
    /// <para>If an operation throws an exception, execution stops and the exception is propagated.</para>
    /// <para>If cancellation is requested, the current operation completes normally, then execution stops.</para>
    /// </remarks>
    public override async Task<TExecutorInputOutput> ExecuteAsync(TExecutorInputOutput input, CancellationToken cancellationToken)
    {
        TExecutorInputOutput previousResult = input;
        if (ExecutorState != null)
            UpdateExecutionStatus(ExecutorState with { Started = DateTime.UtcNow, Status = OperationState.Running });

        try
        {
            foreach (var operationItem in Operations.Values)
            {
                cancellationToken.ThrowIfCancellationRequested();

                UpdateOperationItem(Operations[operationItem.Id] with { Started = DateTime.UtcNow, Status = OperationState.Running });

                previousResult = await HandleOperation(Operations[operationItem.Id], previousResult, cancellationToken);

                if (Operations[operationItem.Id].Status != OperationState.Skipped)
                    UpdateOperationItem(Operations[operationItem.Id] with { Completed = DateTime.UtcNow, Status = OperationState.Completed });
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or TaskCanceledException)
        {
            if (ExecutorState != null)
                UpdateExecutionStatus(ExecutorState with { Status = OperationState.Cancelled });

            throw;
        }
        finally
        {
            if (ExecutorState != null && ExecutorState.Status != OperationState.Cancelled)
                UpdateExecutionStatus(ExecutorState with { Completed = DateTime.UtcNow, Status = OperationState.Completed });
        }

        return previousResult;
    }
}
