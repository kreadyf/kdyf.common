using kdyf.Notifications.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace kdyf.Notifications.Integration
{
    /// <summary>
    /// Default implementation of the notification builder for configuring services.
    /// </summary>
    public class DefaultNotificationBuilder : INotificationBuilder
    {
        /// <summary>
        /// Gets the service collection for dependency injection configuration.
        /// </summary>
        public IServiceCollection Services { get; }

        /// <summary>
        /// Gets the application configuration.
        /// </summary>
        public IConfiguration Configuration { get; }

        /// <summary>
        /// Gets the list of registered emitter types.
        /// </summary>
        public IList<Type> Emitters { get; }

        /// <summary>
        /// Gets the list of registered receiver types.
        /// </summary>
        public IList<Type> Receivers { get; }

        /// <summary>
        /// Gets the notification options for configuring system behavior.
        /// </summary>
        public NotificationOptions Options { get; }

        /// <summary>
        /// Gets or sets additional properties for extensions (e.g., Redis-specific options).
        /// </summary>
        public IDictionary<string, object> Properties { get; }

        /// <summary>
        /// Creates a new instance of the notification builder.
        /// </summary>
        /// <param name="services">The service collection to configure.</param>
        /// <param name="configuration">The application configuration.</param>
        public DefaultNotificationBuilder(IServiceCollection services, IConfiguration configuration)
        {
            Services = services;
            Configuration = configuration;
            Emitters = new List<Type>();
            Receivers = new List<Type>();
            Options = new NotificationOptions(); // Default options, can be configured via extension methods
            Properties = new Dictionary<string, object>(); // For extension-specific configuration
        }
    }
}
