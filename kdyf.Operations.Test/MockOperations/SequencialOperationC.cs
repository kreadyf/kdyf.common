using kdyf.Operations.Extensions.Attributes;
using kdyf.Operations.Integration;

namespace kdyf.Operations.Test.MockOperations;

[OperationDescriptor("Secuencial Operation C", "Description Secuencial Operation C")]
public class SequencialOperationC : IOperation<SequencialOperationC.ISequencialCInOut>
{
    public interface ISequencialCInOut
    {
        public string Shared { get; set; }
        public int C { get; set; }
    }

    public event IOperation<ISequencialCInOut>.StatusChangedEventHandler? OnStatusChanged;

    public Task<ISequencialCInOut> ExecuteAsync(ISequencialCInOut input, CancellationToken cancellationToken)
    {
        input.Shared += $"(C {input.C++})";

        return Task.FromResult(input);
    }

    public void Dispose()
    {
    }
}
