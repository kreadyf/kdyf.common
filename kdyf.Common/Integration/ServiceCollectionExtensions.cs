using kdyf.Common.Models;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace kdyf.Common.Integration
{
    /// <summary>
    /// Extension methods for <see cref="IServiceCollection"/> to track invocations.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Checks if a method or operation has already been invoked with the given identifier.
        /// If not invoked, it records the invocation and returns false.
        /// Uses an <see cref="InvocationTracker"/> singleton to maintain state across calls.
        /// </summary>
        /// <param name="services">The service collection to track against.</param>
        /// <param name="identifier">Unique identifier for the invocation to track.</param>
        /// <returns>True if already invoked; false if this is the first invocation.</returns>
        public static bool AlreadyInvoked(this IServiceCollection services, string identifier)
        {
            var trackerDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(InvocationTracker));

            if (trackerDescriptor == null)
            {
                trackerDescriptor = new ServiceDescriptor(typeof(InvocationTracker), new InvocationTracker());
                services.Add(trackerDescriptor);
            }

            var tracker = (InvocationTracker)trackerDescriptor!.ImplementationInstance!;

            return !tracker.InvokedMethods.TryAdd(identifier, identifier);
        }
    }
}
