namespace kdyf.Operations.Extensions;
public static class ExceptionExtensions
{
    public static Exception GetMostInnerException(this Exception ex)
    {
        if (ex == null) throw new ArgumentNullException(nameof(ex));

        while (ex.InnerException != null)
        {
            ex = ex.InnerException;
        }

        return ex;
    }

    /// <summary>
    /// Converts an Exception to a serializable ErrorDetails record.
    /// </summary>
    /// <param name="ex">The exception to convert.</param>
    /// <returns>An ErrorDetails record containing the exception information, or null if ex is null.</returns>
    public static ErrorDetails? ToErrorDetails(this Exception? ex)
    {
        if (ex == null) return null;

        return new ErrorDetails(
            Message: ex.Message,
            Type: ex.GetType().FullName ?? ex.GetType().Name,
            StackTrace: ex.StackTrace,
            InnerError: ex.InnerException?.ToErrorDetails()
        );
    }
}
