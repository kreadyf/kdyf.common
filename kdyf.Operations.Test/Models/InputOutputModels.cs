using kdyf.Operations.Extensions.Attributes;
using kdyf.Operations.Test.MockOperations;
using static kdyf.Operations.Test.MockOperations.ProducerOperation;
using static kdyf.Operations.Test.MockOperations.SequencialOperationA;
using static kdyf.Operations.Test.MockOperations.SequencialOperationB;
using static kdyf.Operations.Test.MockOperations.SequencialOperationC;
using static kdyf.Operations.Test.MockOperations.SequencialOperationD;

namespace kdyf.Operations.Test.Models;

public class InputOutput : ISequencialAInOut, ISequencialBInOut, ISequencialCInOut, ISequencialDInOut
{
    public string Shared { get; set; } = string.Empty;
    public int D { get; set; }
    public int C { get; set; }
    public int B { get; set; }
    public int A { get; set; }
}

[OperationDescriptor("Sequence Input Output", "Description Sequence Input Output")]
public class SequenceInputOutput : ISequencialBInOut, ISequencialCInOut, ISequencialDInOut
{
    public string Shared { get; set; } = string.Empty;
    public int C { get; set; }
    public int B { get; set; }
    public int D { get; set; }

}

[OperationDescriptor("Async Pipeline Input Output", "Description Async Pipeline Input Output")]
public class AsyncPipelineInputOutput : IAsyncProducerInputOutput, ISequencialAInOut, ISequencialCInOut, ISequencialDInOut
{
    public string Shared { get; set; } = string.Empty;
    public int A { get; set; }
    public int C { get; set; }
    public int Factor { get; set; }
    public int Start { get; set; }
    public int D { get; set; }

}
