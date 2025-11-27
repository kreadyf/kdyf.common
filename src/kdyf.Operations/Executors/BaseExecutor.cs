
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.ExceptionServices;
using kdyf.Operations.Extensions;
using kdyf.Operations.Integration;

namespace kdyf.Operations.Executors;
public abstract class BaseExecutor<TExecutorInputOutput> : ISubsequentExecutor<TExecutorInputOutput>, IDisposable
{
    // Static caches for reflection results - shared across all executor instances for performance
    private static readonly ConcurrentDictionary<Type, MethodInfo> _methodCache = new();
    private static readonly ConcurrentDictionary<Type, EventInfo?> _eventCache = new();

    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Gets the dictionary of operation execution statuses indexed by operation ID.
    /// </summary>
    /// <remarks>
    /// <para><b>Thread Safety:</b></para>
    /// <list type="bullet">
    ///   <item><description>Do NOT call <see cref="ExecuteAsync"/> concurrently on the same executor instance.</description></item>
    ///   <item><description>Internal concurrent access within <see cref="AsyncPipelineExecutor{TExecutorInputOutput}"/> is safe
    ///   because each concurrent task accesses a different key in the dictionary.</description></item>
    ///   <item><description>Dictionary is NOT thread-safe for external concurrent access.</description></item>
    /// </list>
    /// <para><b>Insertion Order:</b></para>
    /// <para>Dictionary maintains insertion order for enumeration (guaranteed by .NET implementation).</para>
    /// </remarks>
    public Dictionary<Guid, ExecutionStatus> Operations { get; } = new();

    public ExecutionStatus? ExecutorState { get; set; }

    private record ActionMappingBase();
    private record ActionMapping<TInOut>(Func<TExecutorInputOutput, TInOut> MapIn, Action<TInOut, TExecutorInputOutput> MapOut, Func<TExecutorInputOutput, bool>? Condition) : ActionMappingBase;

    private Dictionary<Guid, ActionMappingBase> _actionMappings = new();
    private List<(IOperation Operation, Delegate Handler, EventInfo EventInfo)> _eventHandlers = new();
    private readonly Dictionary<Guid, Delegate> _statusChangeHandlers = new();

    public event IExecutor.ExecutionStatusChangedEventHandler? OnExecutionStatusChanged;

    private List<IDisposable> _disposables { get; } = new();

    protected BaseExecutor(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public void SetOperatorExecutor(string containerName)
    {
        ExecutorState = new ExecutionStatus(null, Guid.NewGuid(), containerName);
    }

    public void SetExecutorError(Exception ex)
    {
        UpdateExecutionStatus(ExecutorState with { Status = OperationState.Faulted, Error = ex, SerializableError = ex.ToErrorDetails(), Completed = DateTime.UtcNow });
    }

    public ISubsequentExecutor<TExecutorInputOutput> Add<TOperation, TInputOutput>(Func<IServiceProvider, IServiceScope>? scopeFactory = null) where TOperation : IOperation<TInputOutput>
    {
        var sp = ResolveServiceProvider(scopeFactory);

        var operation = sp.GetRequiredService<TOperation>();



        var id = Guid.NewGuid();

        var friendlyNameAndDescription = typeof(TOperation).GetFriendlyTypeNameAndDescription(operation);
        Operations[id] = new ExecutionStatus(operation, id, friendlyNameAndDescription.Item1, friendlyNameAndDescription.Item2);

        return this;
    }

    public void Dispose()
    {
        // Remove all event handlers to prevent memory leaks
        foreach (var (operation, handler, eventInfo) in _eventHandlers)
        {
            eventInfo.RemoveEventHandler(operation, handler);
        }
        _eventHandlers.Clear();

        // Clear handler tracking
        _statusChangeHandlers.Clear();

        // Clear action mappings to prevent unbounded dictionary growth
        _actionMappings.Clear();

        // Dispose service scopes
        _disposables.ForEach(d => d.Dispose());
    }

    public ISubsequentExecutor<TExecutorInputOutput> AddSequence<TInputOutput>(Func<TExecutorInputOutput, TInputOutput> mapInto, Func<ISequenceStartExecutor<TInputOutput>, IExecutor<TInputOutput>> exec, Action<TInputOutput, TExecutorInputOutput> mapOut, Func<IServiceProvider, IServiceScope>? scopeFactory = null)
    {
        return AddExecutor(null, mapInto, exec, mapOut, scopeFactory);
    }

    public ISubsequentExecutor<TExecutorInputOutput> AddSequence<TInputOutput>(Func<TExecutorInputOutput, bool>? condition, Func<TExecutorInputOutput, TInputOutput> mapInto, Func<ISequenceStartExecutor<TInputOutput>, IExecutor<TInputOutput>> exec, Action<TInputOutput, TExecutorInputOutput> mapOut, Func<IServiceProvider, IServiceScope>? scopeFactory = null)
    {
        return AddExecutor(condition, mapInto, exec, mapOut, scopeFactory);
    }

    public ISubsequentExecutor<TExecutorInputOutput> AddAsyncPipeline<TInputOutput>(Func<TExecutorInputOutput, TInputOutput> mapInto, Func<IAsyncPipelineStartExecutor<TInputOutput>, IExecutor<TInputOutput>> exec, Action<TInputOutput, TExecutorInputOutput> mapOut, Func<IServiceProvider, IServiceScope>? scopeFactory = null)
    {
        return AddExecutor(null, mapInto, exec, mapOut, scopeFactory);
    }

    protected ISubsequentExecutor<TExecutorInputOutput> AddExecutor<TExecutor, TInputOutput>(Func<TExecutorInputOutput, bool>? condition, Func<TExecutorInputOutput, TInputOutput> mapInto, Func<TExecutor, IExecutor<TInputOutput>> exec, Action<TInputOutput, TExecutorInputOutput> mapOut, Func<IServiceProvider, IServiceScope>? scopeFactory = null)
        where TExecutor : IExecutor<TInputOutput>
    {
        var sp = ResolveServiceProvider(scopeFactory);

        var executor = sp.GetRequiredService<TExecutor>();

        exec(executor);

        var id = Guid.NewGuid();

        var friendlyNameAndDescription = typeof(TExecutor).GetFriendlyTypeNameAndDescription(executor);
        Operations[id] = new ExecutionStatus(executor, id, friendlyNameAndDescription.Item1, friendlyNameAndDescription.Item2);
        _actionMappings.Add(id, new ActionMapping<TInputOutput>(mapInto, mapOut, condition));

        executor.OnExecutionStatusChanged += (updatedItem) => this.OnExecutionStatusChanged?.Invoke(updatedItem);

        return this;
    }

    protected IServiceProvider ResolveServiceProvider(Func<IServiceProvider, IServiceScope>? scopeFactory = null)
    {
        if (scopeFactory == null)
            return _serviceProvider;

        var scope = scopeFactory(_serviceProvider);
        _disposables.Add(scope);

        return scope.ServiceProvider;
    }

    internal async Task<TExecutorInputOutput> HandleOperation(ExecutionStatus operationItem, TExecutorInputOutput input, CancellationToken cancellationToken)
    {
        try
        {
            if (operationItem.Operation is IExecutor)
                return await HandleExecutor(operationItem, input, cancellationToken);

            return await ExecuteGenericOperation(input, operationItem.Id, cancellationToken);
        }
        catch (Exception ex) when (ex is OperationCanceledException or TaskCanceledException)
        {
            UpdateOperationItem(Operations[operationItem.Id] with { Status = OperationState.Cancelled });
            throw;
        }
    }

    private async Task<TExecutorInputOutput> HandleExecutor(ExecutionStatus operationItem, TExecutorInputOutput input, CancellationToken cancellationToken)
    {
        if (!_actionMappings.TryGetValue(operationItem.Id, out var actionMapping))
            throw new InvalidOperationException($"Action mapping not found for operation {operationItem.Id}");

        // Optimize: Use pattern matching to avoid DynamicInvoke when types match
        // This is 10-100x faster than using reflection
        if (actionMapping is ActionMapping<TExecutorInputOutput> typedMapping)
        {
            // Fast path: No type conversion needed, direct delegate calls
            if (typedMapping.Condition != null && !typedMapping.Condition(input))
            {
                UpdateOperationItem(Operations[operationItem.Id] with { Status = OperationState.Skipped });
                return input;
            }

            UpdateOperationItem(Operations[operationItem.Id] with { Started = DateTime.UtcNow, Status = OperationState.Running });

            var result = await ExecuteGenericOperation(typedMapping.MapIn(input), operationItem.Id, cancellationToken);

            UpdateOperationItem(Operations[operationItem.Id] with { Completed = DateTime.UtcNow, Status = OperationState.Completed });

            typedMapping.MapOut(result, input);

            return input;
        }
        else
        {
            // Slow path: Fall back to reflection for type conversion
            return await HandleExecutorWithReflection(operationItem, actionMapping, input, cancellationToken);
        }
    }

    private async Task<TExecutorInputOutput> HandleExecutorWithReflection(ExecutionStatus operationItem, ActionMappingBase actionMapping, TExecutorInputOutput input, CancellationToken cancellationToken)
    {
        var conditionMethod = (Func<TExecutorInputOutput, bool>?)actionMapping.GetType().GetProperty("Condition")!.GetValue(actionMapping);

        if (conditionMethod != null && !conditionMethod.Invoke(input))
        {
            UpdateOperationItem(Operations[operationItem.Id] with { Status = OperationState.Skipped });
            return input;
        }

        var mapInMethod = actionMapping.GetType().GetProperty("MapIn")!.GetValue(actionMapping);
        var mapOutMethod = actionMapping.GetType().GetProperty("MapOut")!.GetValue(actionMapping);

        var mappedInput = ((Delegate)mapInMethod!).DynamicInvoke(input);

        UpdateOperationItem(Operations[operationItem.Id] with { Started = DateTime.UtcNow, Status = OperationState.Running });

        var result = await ExecuteGenericOperation(mappedInput, operationItem.Id, cancellationToken);

        UpdateOperationItem(Operations[operationItem.Id] with { Completed = DateTime.UtcNow, Status = OperationState.Completed });

        ((Delegate)mapOutMethod!).DynamicInvoke(result, input);

        return input;
    }


    private async Task<TInputOutput> ExecuteGenericOperation<TInputOutput>(TInputOutput input, Guid operationId, CancellationToken cancellationToken)
    {
        try
        {
            if (!Operations.TryGetValue(operationId, out var operationItem))
                throw new InvalidOperationException($"Operation with Id {operationId} not found.");

            var operation = operationItem.Operation;
            var method = GetMethodByOperation<TInputOutput>(operation, operationId);
            var task = (Task)method!.Invoke(operation, new object[] { input!, cancellationToken })!;

            return await task.ContinueWith(t =>
            {
                if (t.IsFaulted && t.Exception != null)
                {
                    var exception = t.Exception.GetMostInnerException();
                    UpdateOperationItem(Operations[operationId] with { Status = OperationState.Faulted, Error = exception, SerializableError = exception.ToErrorDetails() });
                    ExceptionDispatchInfo.Capture(exception).Throw();
                }

                var resultProperty = t.GetType().GetProperty("Result")!;
                return (TInputOutput)resultProperty.GetValue(t)!;
            }, cancellationToken)!;
        }
        catch (TargetInvocationException ex)
        {
            // Unwrap TargetInvocationException from method.Invoke() and re-throw inner exception
            var exception = ex.GetMostInnerException();
            UpdateOperationItem(Operations[operationId] with { Status = OperationState.Faulted, Error = exception, SerializableError = exception.ToErrorDetails() });
            ExceptionDispatchInfo.Capture(exception).Throw();
            throw;
        }
    }

    protected MethodInfo GetMethodByOperation<TInputOutput>(IOperation operation, Guid operationId)
    {
        var operationType = operation.GetType();

        // Cache method lookup - only do reflection once per operation type
        var method = _methodCache.GetOrAdd(operationType, type =>
        {
            var methodInfo = type.GetMethod(nameof(IOperation<TInputOutput>.ExecuteAsync));
            if (methodInfo == null)
                throw new InvalidOperationException($"Method ExecuteAsync not found on {type.Name}");
            return methodInfo;
        });

        // Only register event handler once per operation instance
        if (!_statusChangeHandlers.ContainsKey(operationId))
        {
            // Cache event lookup - only do reflection once per operation type
            var eventInfo = _eventCache.GetOrAdd(operationType, type =>
                type.GetEvent(nameof(IOperation<TInputOutput>.OnStatusChanged)));

            if (eventInfo != null)
            {
                var handlerMethod = new Action<OperationStatus>(updatedItem =>
                {
                    if (Operations.TryGetValue(operationId, out var item))
                    {
                        var updated = item with
                        {
                            CompletionPercentage = updatedItem.CompletionPercentage,
                            Message = updatedItem.Message
                        };
                        UpdateOperationItem(updated);
                    }
                });

                var handlerType = eventInfo.EventHandlerType;
                var inlineDelegate = Delegate.CreateDelegate(handlerType!, handlerMethod.Target, handlerMethod.Method);
                eventInfo.AddEventHandler(operation, inlineDelegate);

                // Track the handler so we can remove it later in Dispose and avoid duplicate registration
                _eventHandlers.Add((operation, inlineDelegate, eventInfo));
                _statusChangeHandlers[operationId] = inlineDelegate;
            }
        }

        return method;
    }

    protected ExecutionStatus UpdateOperationItem(ExecutionStatus newItem)
    {
        newItem = newItem with { Updated = DateTime.UtcNow };
        if (Operations[newItem.Id].Started != null)
        {
            newItem = newItem with { Started = Operations[newItem.Id].Started };
        }
        Operations[newItem.Id] = newItem;
        OnExecutionStatusChanged?.Invoke(newItem);
        return newItem;
    }

    protected ExecutionStatus UpdateExecutionStatus(ExecutionStatus newItem)
    {
        newItem = newItem with { Updated = DateTime.UtcNow };
        ExecutorState = newItem;
        OnExecutionStatusChanged?.Invoke(newItem);
        return newItem;

    }

    public abstract Task<TExecutorInputOutput> ExecuteAsync(TExecutorInputOutput input, CancellationToken cancellationToken);
}

