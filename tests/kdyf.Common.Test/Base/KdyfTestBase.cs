using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;

namespace kdyf.Common.Test.Base
{
    /// <summary>
    /// Base class for unit tests that require dependency injection.
    /// Provides infrastructure for configuring and managing a service provider.
    /// </summary>
    [TestClass]
    public abstract class KdyfTestBase
    {
        /// <summary>
        /// Gets the service provider configured for the current test.
        /// </summary>
        protected ServiceProvider? ServiceProvider { get; private set; }

        /// <summary>
        /// Gets the configuration available for the current test.
        /// </summary>
        protected IConfiguration Configuration { get; private set; } = default!;

        /// <summary>
        /// Initializes the service provider before each test.
        /// </summary>
        [TestInitialize]
        public void BaseSetup()
        {
            var services = new ServiceCollection();
                        
            Configuration = BuildConfiguration();
            
            services.AddSingleton<IConfiguration>(sp => Configuration);
            ConfigureServices(services, Configuration);

            ServiceProvider = services.BuildServiceProvider();

            OnSetup();
        }

        /// <summary>
        /// Cleans up the service provider after each test.
        /// </summary>
        [TestCleanup]
        public void BaseCleanup()
        {
            OnCleanup();
            ServiceProvider?.Dispose();
        }

        /// <summary>
        /// Builds the default configuration for the tests.
        /// Override this if you need custom sources (e.g., environment vars, JSON files).
        /// </summary>
        protected virtual IConfiguration BuildConfiguration()
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                .AddJsonFile("appsettings.Test.json", optional: true, reloadOnChange: false)
                //.AddUserSecrets(System.Reflection.Assembly.GetExecutingAssembly(), optional: true)
                .AddEnvironmentVariables();

            return builder.Build();
        }

        /// <summary>
        /// Configures the services for the test.
        /// Override this method to register services specific to your tests.
        /// </summary>
        /// <param name="services">The service collection to configure.</param>
        /// <param name="configuration">The configuration object for this test.</param>
        protected abstract void ConfigureServices(IServiceCollection services, IConfiguration configuration);

        /// <summary>
        /// Called after the service provider is built but before the test runs.
        /// Override this method to perform additional setup.
        /// </summary>
        protected virtual void OnSetup() { }

        /// <summary>
        /// Called before the service provider is disposed but after the test completes.
        /// Override this method to perform additional cleanup.
        /// </summary>
        protected virtual void OnCleanup() { }

        /// <summary>
        /// Gets a service of type <typeparamref name="T"/> from the service provider.
        /// </summary>
        protected T GetService<T>() where T : notnull
        {
            if (ServiceProvider == null)
                throw new InvalidOperationException("ServiceProvider has not been initialized. Ensure [TestInitialize] has run.");

            return ServiceProvider.GetRequiredService<T>();
        }

        /// <summary>
        /// Gets a service of type <typeparamref name="T"/> from the service provider, or null if not found.
        /// </summary>
        protected T? GetServiceOrNull<T>() where T : class
        {
            if (ServiceProvider == null)
                throw new InvalidOperationException("ServiceProvider has not been initialized. Ensure [TestInitialize] has run.");

            return ServiceProvider.GetService<T>();
        }

        /// <summary>
        /// Creates a new scope for dependency injection.
        /// Useful for testing scoped services.
        /// </summary>
        protected IServiceScope CreateScope()
        {
            if (ServiceProvider == null)
                throw new InvalidOperationException("ServiceProvider has not been initialized. Ensure [TestInitialize] has run.");

            return ServiceProvider.CreateScope();
        }
    }
}
