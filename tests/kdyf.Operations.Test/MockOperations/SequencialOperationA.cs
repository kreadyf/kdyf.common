using kdyf.Operations.Extensions.Attributes;
using kdyf.Operations.Integration;

namespace kdyf.Operations.Test.MockOperations;

[OperationDescriptor("Secuencial Operation A", "Description Secuencial Operation A")]
public class SequencialOperationA : IOperation<SequencialOperationA.ISequencialAInOut>
{
    public interface ISequencialAInOut
    {
        public string Shared { get; set; }
        public int A { get; set; }
    }

    public event IOperation<ISequencialAInOut>.StatusChangedEventHandler? OnStatusChanged;

    public Task<ISequencialAInOut> ExecuteAsync(ISequencialAInOut input, CancellationToken cancellationToken)
    {
        input.Shared += $"(A {input.A++})";

        OnStatusChanged?.Invoke(new OperationStatus(10, "mops"));
        return Task.FromResult(input);
    }

    public void Dispose()
    {
    }
}
