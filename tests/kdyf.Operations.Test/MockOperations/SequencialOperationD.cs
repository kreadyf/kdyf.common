using kdyf.Operations.Extensions.Attributes;
using kdyf.Operations.Integration;

namespace kdyf.Operations.Test.MockOperations;

[OperationDescriptor("Secuencial Operation D", "Description Secuencial Operation D")]
public class SequencialOperationD : IOperation<SequencialOperationD.ISequencialDInOut>
{
    public interface ISequencialDInOut
    {
        public string Shared { get; set; }
        public int D { get; set; }
    }

    public event IOperation<ISequencialDInOut>.StatusChangedEventHandler? OnStatusChanged;

    public Task<ISequencialDInOut> ExecuteAsync(ISequencialDInOut input, CancellationToken cancellationToken)
    {
        input.Shared += $"(D {input.D++})";


        if (input.D > 6)
            throw new InvalidOperationException($"Invalid Input");

        return Task.FromResult(input);
    }

    public void Dispose()
    {
    }
}
