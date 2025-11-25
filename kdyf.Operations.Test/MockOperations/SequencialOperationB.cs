using kdyf.Operations.Extensions.Attributes;
using kdyf.Operations.Integration;

namespace kdyf.Operations.Test.MockOperations;

[OperationDescriptor("Secuencial Operation B", "Description Secuencial Operation B")]
public class SequencialOperationB : IOperation<SequencialOperationB.ISequencialBInOut>
{
    public interface ISequencialBInOut
    {
        public string Shared { get; set; }
        public int B { get; set; }
    }

    public event IOperation<ISequencialBInOut>.StatusChangedEventHandler? OnStatusChanged;

    public Task<ISequencialBInOut> ExecuteAsync(ISequencialBInOut input, CancellationToken cancellationToken)
    {
        input.Shared += $"(B {input.B++})";
        return Task.FromResult(input);
    }

    public void Dispose()
    {
    }
}
