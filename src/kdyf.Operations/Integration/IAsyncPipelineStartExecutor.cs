using Microsoft.Extensions.DependencyInjection;

namespace kdyf.Operations.Integration;
public interface IAsyncPipelineStartExecutor<TExecutorInputOutput> : IExecutor<TExecutorInputOutput>
{
    IAsyncPipelineExecutor<TExecutorInputOutput> Add<TOperation, TInputOutput>(Func<IServiceProvider, IServiceScope>? scopeFactory = null)
        where TOperation : IAsyncProducerOperation<TInputOutput>;
}
