using kdyf.Operations.Integration;

namespace kdyf.Operations.Extensions;
public static class IExecutorExtensions
{
    public static List<ExecutionStatus> GetExecutionTree(this IExecutor @this)
    {
        return @this.Operations.Values.Select(op =>
        {
            return op with { Nodes = (op.Operation as IExecutor)?.GetExecutionTree() ?? new() };
        })
        .ToList();
    }
}


