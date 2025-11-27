namespace kdyf.Operations.Integration;

public interface IOperation
{
}

public interface IOperation<TInputOutput> : IOperation
{
    public Task<TInputOutput> ExecuteAsync(TInputOutput input, CancellationToken cancellationToken);

    public delegate void StatusChangedEventHandler(OperationStatus updatedItem);
    public event StatusChangedEventHandler? OnStatusChanged;
}

