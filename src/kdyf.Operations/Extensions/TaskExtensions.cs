namespace kdyf.Operations.Extensions;

public static class TaskExtensions
{
    public static Task CancelOnException(this Task @this, CancellationTokenSource cts)
    {
        return @this.ContinueWith(continuationFunction: task =>
        {
            if (task.IsFaulted && task.Exception != null)
            {
                cts.Cancel();
                return Task.FromException(task.Exception);
            }
            return task;
        }, TaskContinuationOptions.ExecuteSynchronously).Unwrap();

    }
}
