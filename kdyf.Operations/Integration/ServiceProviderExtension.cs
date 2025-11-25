using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using kdyf.Operations.Executors;

namespace kdyf.Operations.Integration;
public static class ServiceProviderExtension
{
    public static IServiceCollection AddKdyfOperations(this IServiceCollection services, params Assembly[] assemblies)
    {
        services.AddTransient(typeof(ISequenceStartExecutor<>), typeof(SequenceExecutor<>));
        services.AddTransient(typeof(IAsyncPipelineStartExecutor<>), typeof(AsyncPipelineExecutor<>));

        var assembliesList = assemblies.ToList();
        assembliesList.AddRange(Assembly.GetEntryAssembly()!.GetReferencedAssemblies().Select(Assembly.Load));
        assembliesList.Add(Assembly.GetEntryAssembly()!);

        assembliesList = assembliesList
            .Where(a =>
            {
                return a.FullName != null
                    && !a.FullName.StartsWith("Microsoft.")
                    && !a.FullName.StartsWith("System.")
                    && !a.FullName.StartsWith("netstandard")
                    && !a.FullName.StartsWith("Newtonsoft.")
                    && !a.FullName.StartsWith("IdentityServer4")
                    && !a.FullName.StartsWith("Anonymously")
                    && !a.FullName.StartsWith("Swashbuckle");
            }).ToList();

        var operationTypes = assembliesList.SelectMany(assembly => assembly.GetTypes()
            .Where(type => typeof(IOperation).IsAssignableFrom(type) &&
                           !typeof(IExecutor).IsAssignableFrom(type) &&
                           type.IsClass && !type.IsAbstract));

        foreach (var type in operationTypes)
        {
            // Handle non-generic types
            services.AddTransient(type);
        }

        return services;
    }

    public static ISequenceStartExecutor<TExecutorInputOutput> CreateCommonOperationExecutor<TExecutorInputOutput>(this IServiceProvider serviceProvider)
    {
        return serviceProvider.GetRequiredService<ISequenceStartExecutor<TExecutorInputOutput>>();
    }
}
