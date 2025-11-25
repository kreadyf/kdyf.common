namespace kdyf.Operations.Integration;
public interface IAsyncProducerOperation<TInputOutput> : IOperation
{
    public IAsyncEnumerable<TInputOutput> ExecuteAsync(TInputOutput input, CancellationToken cancellationToken);
    public event IOperation<TInputOutput>.StatusChangedEventHandler? OnStatusChanged;
}
