using Microsoft.Extensions.DependencyInjection;

namespace kdyf.Operations.Integration;

/// <summary>
/// Represents a base executor that can execute a collection of operations.
/// </summary>
/// <remarks>
/// <para><b>Thread Safety:</b></para>
/// <para>Executor instances are NOT thread-safe. Do NOT call <see cref="IExecutor{TExecutorInputOutput}.ExecuteAsync"/>
/// concurrently on the same executor instance.</para>
/// </remarks>
public interface IExecutor : IOperation
{
    /// <summary>
    /// Gets the dictionary of operation execution statuses indexed by operation ID.
    /// </summary>
    /// <remarks>
    /// <para><b>Thread Safety:</b></para>
    /// <list type="bullet">
    ///   <item><description>Do NOT access this dictionary concurrently from multiple threads.</description></item>
    ///   <item><description>Internal concurrent access within AsyncPipelineExecutor is safe because each
    ///   concurrent task accesses a different key in the dictionary.</description></item>
    /// </list>
    /// <para><b>Insertion Order:</b></para>
    /// <para>Dictionary maintains insertion order for enumeration (guaranteed by .NET implementation).</para>
    /// </remarks>
    public Dictionary<Guid, ExecutionStatus> Operations { get; }

    /// <summary>
    /// Gets or sets the execution status of the executor itself.
    /// </summary>
    public ExecutionStatus? ExecutorState { get; set; }

    /// <summary>
    /// Delegate for handling execution status change events.
    /// </summary>
    /// <param name="updatedItem">The updated execution status.</param>
    public delegate void ExecutionStatusChangedEventHandler(ExecutionStatus updatedItem);

    /// <summary>
    /// Event raised when the execution status of any operation or the executor itself changes.
    /// </summary>
    public event ExecutionStatusChangedEventHandler? OnExecutionStatusChanged;
}

/// <summary>
/// Represents a generic executor that processes input and produces output of type <typeparamref name="TExecutorInputOutput"/>.
/// </summary>
/// <typeparam name="TExecutorInputOutput">The type of input and output data processed by the executor.</typeparam>
/// <remarks>
/// <para><b>Thread Safety:</b></para>
/// <para>Do NOT call <see cref="ExecuteAsync"/> concurrently on the same executor instance.
/// Each executor instance should only be used by one caller at a time.</para>
/// </remarks>
public interface IExecutor<TExecutorInputOutput> : IExecutor
{
    /// <summary>
    /// Executes all configured operations with the provided input asynchronously.
    /// </summary>
    /// <param name="input">The input data to be processed by the operations.</param>
    /// <param name="cancellationToken">The cancellation token to cancel the execution.</param>
    /// <returns>A task representing the asynchronous operation, with the processed output.</returns>
    /// <remarks>
    /// <para><b>Thread Safety:</b></para>
    /// <para>This method is NOT thread-safe. Do NOT call this method concurrently on the same executor instance.</para>
    /// <para>Create separate executor instances for concurrent executions.</para>
    /// </remarks>
    public Task<TExecutorInputOutput> ExecuteAsync(TExecutorInputOutput input, CancellationToken cancellationToken);
}

public interface ISubsequentExecutor<TExecutorInputOutput> : IExecutor<TExecutorInputOutput>
{
    ISubsequentExecutor<TExecutorInputOutput> Add<TOperation, TInputOutput>(Func<IServiceProvider, IServiceScope>? scopeFactory = null)
        where TOperation : IOperation<TInputOutput>;

    ISubsequentExecutor<TExecutorInputOutput> AddSequence<TInputOutput>(Func<TExecutorInputOutput, TInputOutput> mapInto, Func<ISequenceStartExecutor<TInputOutput>, IExecutor<TInputOutput>> exec, Action<TInputOutput, TExecutorInputOutput> mapOut, Func<IServiceProvider, IServiceScope>? scopeFactory = null);
    ISubsequentExecutor<TExecutorInputOutput> AddSequence<TInputOutput>(Func<TExecutorInputOutput, bool>? condition, Func<TExecutorInputOutput, TInputOutput> mapInto, Func<ISequenceStartExecutor<TInputOutput>, IExecutor<TInputOutput>> exec, Action<TInputOutput, TExecutorInputOutput> mapOut, Func<IServiceProvider, IServiceScope>? scopeFactory = null);
    ISubsequentExecutor<TExecutorInputOutput> AddAsyncPipeline<TInputOutput>(Func<TExecutorInputOutput, TInputOutput> mapInto, Func<IAsyncPipelineStartExecutor<TInputOutput>, IExecutor<TInputOutput>> exec, Action<TInputOutput, TExecutorInputOutput> mapOut, Func<IServiceProvider, IServiceScope>? scopeFactory = null);
    public void SetOperatorExecutor(string containerName);
    public void SetExecutorError(Exception ex);
}
