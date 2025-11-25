using kdyf.Operations.Extensions.Attributes;
using kdyf.Operations.Integration;
using System.Runtime.CompilerServices;

namespace kdyf.Operations.Test.MockOperations;

[OperationDescriptor("Producer Operation", "Description Producer Operation")]
public class ProducerOperation : IAsyncProducerOperation<ProducerOperation.IAsyncProducerInputOutput>
{
    public event IOperation<IAsyncProducerInputOutput>.StatusChangedEventHandler? OnStatusChanged;

    public async IAsyncEnumerable<IAsyncProducerInputOutput> ExecuteAsync(
        IAsyncProducerInputOutput input,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        int total = input.Start + 10 * input.Factor;
        for (int i = input.Start; i <= total; i += input.Factor)
        {
            cancellationToken.ThrowIfCancellationRequested();

            input.A = i;

            OnStatusChanged?.Invoke(new OperationStatus((i / total) * 100, $"{i}/{total}"));

            yield return input;
            await Task.Delay(100, cancellationToken); // Reduced delay for faster tests
        }
    }

    public interface IAsyncProducerInputOutput
    {
        public int Factor { get; set; }
        public int Start { get; set; }
        public int A { get; set; }
    }

    public void Dispose()
    {
    }
}
