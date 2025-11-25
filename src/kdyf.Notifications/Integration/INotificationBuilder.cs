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
    /// Builder interface for configuring notification services and dependencies.
    /// Tracks registered emitter and receiver types for validation and inspection.
    /// </summary>
    public interface INotificationBuilder
    {
        /// <summary>
        /// Gets the service collection for dependency injection configuration.
        /// </summary>
        IServiceCollection Services { get; }

        /// <summary>
        /// Gets the application configuration.
        /// </summary>
        IConfiguration Configuration { get; }

        /// <summary>
        /// Gets the list of registered emitter types.
        /// Used to track which notification emitters have been configured.
        /// </summary>
        IList<Type> Emitters { get; }

        /// <summary>
        /// Gets the list of registered receiver types.
        /// Used to track which notification receivers have been configured.
        /// </summary>
        IList<Type> Receivers { get; }

        /// <summary>
        /// Gets the notification options for configuring system behavior.
        /// </summary>
        NotificationOptions Options { get; }

        /// <summary>
        /// Gets or sets additional properties for extensions (e.g., Redis-specific options).
        /// This allows transport-specific configuration without coupling the core builder.
        /// </summary>
        IDictionary<string, object> Properties { get; }
    }
}
