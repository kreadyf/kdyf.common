using kdyf.Operations.Executors;
using kdyf.Operations.Integration;
using Microsoft.Extensions.DependencyInjection;

namespace kdyf.Operations.Test;

[TestClass]
public sealed class CancellationLifetimeTests
{
    [TestMethod]
    public async Task Sequence_cancellation_waits_for_operation_cleanup_before_returning()
    {
        var operation = new OperationWithAsyncCleanup();
        using var services = new ServiceCollection().AddSingleton(operation).BuildServiceProvider();
        using var executor = new SequenceExecutor<object>(services);
        executor.SetOperatorExecutor("Cancellation lifetime");
        executor.Add<OperationWithAsyncCleanup, object>();
        using var cancellation = new CancellationTokenSource();
        var execution = executor.ExecuteAsync(new object(), cancellation.Token);
        await operation.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        cancellation.Cancel();
        await operation.CleanupStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        bool returnedBeforeCleanup;
        try
        {
            await Task.WhenAny(execution, Task.Delay(100));
            returnedBeforeCleanup = execution.IsCompleted;
        }
        finally
        {
            operation.ReleaseCleanup.SetResult();
        }

        await Assert.ThrowsExceptionAsync<TaskCanceledException>(() => execution);
        Assert.IsFalse(returnedBeforeCleanup, "The caller must not dispose request services while the operation is still cleaning up.");
        Assert.IsTrue(operation.CleanupCompleted);
    }

    public sealed class OperationWithAsyncCleanup : IOperation<object>
    {
        public event IOperation<object>.StatusChangedEventHandler? OnStatusChanged { add { } remove { } }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CleanupStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseCleanup { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool CleanupCompleted { get; private set; }

        public async Task<object> ExecuteAsync(object input, CancellationToken cancellationToken)
        {
            Started.SetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            finally
            {
                CleanupStarted.SetResult();
                await ReleaseCleanup.Task;
                CleanupCompleted = true;
            }
            return input;
        }
    }
}
