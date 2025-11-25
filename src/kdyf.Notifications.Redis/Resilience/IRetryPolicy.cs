namespace kdyf.Notifications.Redis.Resilience
{
    /// <summary>
    /// Abstraction for retry policies that handle transient failures.
    /// Enables dependency injection and testability for retry logic.
    /// </summary>
    public interface IRetryPolicy
    {
        /// <summary>
        /// Executes an operation with retry logic for transient failures.
        /// </summary>
        /// <typeparam name="T">The return type of the operation.</typeparam>
        /// <param name="operation">The operation to execute.</param>
        /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
        /// <returns>The result of the operation.</returns>
        Task<T> ExecuteAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken = default);

        /// <summary>
        /// Executes an operation with retry logic for transient failures (void return).
        /// </summary>
        /// <param name="operation">The operation to execute.</param>
        /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
        Task ExecuteAsync(Func<Task> operation, CancellationToken cancellationToken = default);
    }
}
