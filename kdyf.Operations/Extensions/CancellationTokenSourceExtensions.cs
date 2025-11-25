namespace kdyf.Operations.Extensions;

internal static class CancellationTokenSourceExtensions
{
    /// <summary>
    /// Creates a synchronization task that monitors cancellation tokens and ensures proper
    /// cleanup coordination in the async pipeline executor.
    /// </summary>
    /// <remarks>
    /// This method provides a synchronization barrier that waits until the internal
    /// CancellationTokenSource is cancelled (in the finally block). This ensures all
    /// pipeline operations observe the cancellation before resources are disposed.
    ///
    /// Uses WaitHandle.WaitAny for efficient waiting instead of polling with Task.Delay.
    /// </remarks>
    public static Task CreateLinkedCancellationTokenSource(this CancellationTokenSource @this, CancellationToken cancellationToken)
    {
        var task = Task.Run(() =>
        {
            try
            {
                // Wait efficiently using WaitHandles instead of polling
                // This blocks until either token is cancelled
                var waitHandles = new[]
                {
                    @this.Token.WaitHandle,
                    cancellationToken.WaitHandle
                };

                WaitHandle.WaitAny(waitHandles);

                // If external token was cancelled, cancel the internal one
                if (cancellationToken.IsCancellationRequested && !@this.IsCancellationRequested)
                {
                    @this.Cancel();
                }
            }
            catch (Exception)
            {
                // If anything fails, cancel the internal token
                try { @this.Cancel(); } catch { }
            }
        });

        return task;
    }
}
