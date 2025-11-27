using kdyf.Operations.Integration;
using System.Text.Json.Serialization;

namespace kdyf.Operations;

public record ErrorDetails(string Message, string Type, string? StackTrace, ErrorDetails? InnerError);
public record OperationStatus(int CompletionPercentage, string Message);
public record OperationExecutionStatus(Guid Id, string Name, string Description, DateTime? Started = null, DateTime? Updated = null, int CompletionPercentage = 0, DateTime? Completed = null, string? Message = null, [property: JsonIgnore] Exception? Error = null, ErrorDetails? SerializableError = null, OperationState Status = OperationState.Pending);
public enum OperationState
{
    Pending = 0,
    Running = 1,
    Completed = 2,
    Skipped = 3,
    Faulted = 4,
    Cancelled = 5
}

public record ExecutionStatus(
    [property: JsonIgnore] IOperation Operation,
    Guid Id,
    string Name,
    string Description = "",
    List<ExecutionStatus>? Nodes = null,
    DateTime? Started = null,
    DateTime? Updated = null,
    int CompletionPercentage = 0,
    DateTime? Completed = null,
    string? Message = null,
    [property: JsonIgnore] Exception? Error = null,
    ErrorDetails? SerializableError = null,
    OperationState Status = OperationState.Pending);

