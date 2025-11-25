# kdyf.Common

[![NuGet](https://img.shields.io/nuget/v/kdyf.Common.svg)](https://www.nuget.org/packages/kdyf.Common/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

Common utilities and base classes for KDYF projects. Provides reusable components for dependency injection testing, invocation tracking, and collection extensions.

## Features

- **KdyfTestBase**: Abstract base class for MSTest unit tests with built-in dependency injection
- **InvocationTracker**: Thread-safe tracker for preventing duplicate service registrations
- **Collection Extensions**: Utility methods for IEnumerable operations
- **Service Collection Extensions**: Helper methods for service registration tracking

## Installation

```bash
dotnet add package kdyf.Common
```

## Components

### KdyfTestBase

Abstract base class for unit tests that require dependency injection with MSTest framework.

#### Features

- Automatic service provider setup and teardown
- Configuration management with appsettings.json support
- Built-in lifecycle hooks (OnSetup, OnCleanup)
- Helper methods for service resolution

#### Usage

```csharp
using kdyf.Common.Test.Base;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

[TestClass]
public class MyServiceTests : KdyfTestBase
{
    protected override void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // Register your services
        services.AddTransient<IMyService, MyService>();
        services.AddSingleton<IMyRepository, MyRepository>();
    }

    [TestMethod]
    public void MyService_ShouldDoSomething()
    {
        // Arrange
        var myService = GetService<IMyService>();

        // Act
        var result = myService.DoSomething();

        // Assert
        Assert.IsNotNull(result);
    }
}
```

#### Advanced Usage

```csharp
[TestClass]
public class AdvancedTests : KdyfTestBase
{
    private ILogger<AdvancedTests>? _logger;

    protected override void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddLogging();
        services.AddTransient<IMyService, MyService>();
    }

    protected override void OnSetup()
    {
        // Called after ServiceProvider is built
        _logger = GetService<ILogger<AdvancedTests>>();
        _logger.LogInformation("Test starting");
    }

    protected override void OnCleanup()
    {
        // Called before ServiceProvider is disposed
        _logger?.LogInformation("Test completed");
    }

    [TestMethod]
    public void Test_WithScoping()
    {
        // Create a scope for scoped services
        using var scope = CreateScope();
        var scopedService = scope.ServiceProvider.GetRequiredService<IMyScopedService>();

        // Use the scoped service
        scopedService.DoWork();
    }

    [TestMethod]
    public void Test_WithOptionalService()
    {
        // Get service that might not be registered
        var optionalService = GetServiceOrNull<IOptionalService>();

        if (optionalService != null)
        {
            optionalService.DoOptionalWork();
        }
    }
}
```

#### Configuration Support

The base class automatically loads configuration from:

1. `appsettings.json` (optional)
2. `appsettings.Test.json` (optional)
3. Environment variables

```csharp
protected override void ConfigureServices(IServiceCollection services, IConfiguration configuration)
{
    // Access configuration values
    var connectionString = configuration.GetConnectionString("DefaultConnection");
    var apiKey = configuration["ApiKey"];

    services.AddDbContext<MyDbContext>(options =>
        options.UseSqlServer(connectionString));
}
```

#### Custom Configuration

Override `BuildConfiguration` to customize configuration sources:

```csharp
protected override IConfiguration BuildConfiguration()
{
    var builder = new ConfigurationBuilder()
        .SetBasePath(Directory.GetCurrentDirectory())
        .AddJsonFile("appsettings.json", optional: true)
        .AddJsonFile("appsettings.Development.json", optional: true)
        .AddUserSecrets<MyServiceTests>()
        .AddEnvironmentVariables();

    return builder.Build();
}
```

### InvocationTracker

Thread-safe tracker for recording method invocations to prevent duplicate operations.

#### Purpose

Prevents duplicate service registrations or repeated operations during dependency injection setup.

#### Usage with AlreadyInvoked

```csharp
using kdyf.Common.Integration;
using Microsoft.Extensions.DependencyInjection;

public static class MyServiceExtensions
{
    public static IServiceCollection AddMyFeature(this IServiceCollection services)
    {
        // Prevent duplicate registration
        if (services.AlreadyInvoked(nameof(AddMyFeature)))
        {
            return services; // Already registered, skip
        }

        // Register services
        services.AddSingleton<IMyService, MyService>();
        services.AddTransient<IMyRepository, MyRepository>();

        return services;
    }
}
```

#### Example: Preventing Double Registration

```csharp
var services = new ServiceCollection();

// First call - registers services
services.AddMyFeature();

// Second call - skips registration (already invoked)
services.AddMyFeature();

var serviceProvider = services.BuildServiceProvider();

// Only one instance of IMyService is registered
var myServices = serviceProvider.GetServices<IMyService>();
Assert.AreEqual(1, myServices.Count());
```

#### How It Works

1. `AlreadyInvoked` creates a singleton `InvocationTracker` in the service collection (if not exists)
2. Checks if the identifier has been recorded
3. If not recorded, adds it to the tracker and returns `false` (not invoked before)
4. If already recorded, returns `true` (already invoked)

The tracker is thread-safe and uses `ConcurrentDictionary` internally.

### Collection Extensions

Utility extension methods for `IEnumerable<T>` collections.

#### IsNullOrEmpty

Determines whether an enumerable is null or contains no elements.

```csharp
using System.Linq;

// Check if collection is null or empty
var items = GetItems(); // Could be null or empty
if (items.IsNullOrEmpty())
{
    Console.WriteLine("No items found");
    return;
}

// Process items
foreach (var item in items)
{
    Console.WriteLine(item);
}
```

#### Example: Guard Clauses

```csharp
public void ProcessItems(IEnumerable<string>? items)
{
    if (items.IsNullOrEmpty())
    {
        throw new ArgumentException("Items cannot be null or empty", nameof(items));
    }

    // Safe to process
    foreach (var item in items)
    {
        Process(item);
    }
}
```

#### Example: Conditional Logic

```csharp
public class OrderService
{
    public OrderSummary GetOrderSummary(Order order)
    {
        return new OrderSummary
        {
            OrderId = order.Id,
            HasItems = !order.Items.IsNullOrEmpty(),
            ItemCount = order.Items?.Count() ?? 0,
            Total = order.Items.IsNullOrEmpty()
                ? 0
                : order.Items.Sum(i => i.Price)
        };
    }
}
```

**Note**: `IsNullOrEmpty()` uses `.Any()` internally, which will materialize lazy enumerables. If you need to preserve lazy evaluation, consider using null checks and `.Any()` separately.

## Best Practices

### 1. Use KdyfTestBase for Consistent Testing

Standardize your test infrastructure across all test projects:

```csharp
// All test classes inherit from KdyfTestBase
[TestClass]
public class MyServiceTests : KdyfTestBase
{
    protected override void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // Consistent service registration pattern
        services.AddMyApplicationServices(configuration);
    }
}
```

### 2. Prevent Duplicate Registrations

Use `AlreadyInvoked` in extension methods that might be called multiple times:

```csharp
public static IServiceCollection AddMyInfrastructure(this IServiceCollection services)
{
    if (services.AlreadyInvoked(nameof(AddMyInfrastructure)))
        return services;

    // Register once
    services.AddHttpClient();
    services.AddMemoryCache();

    return services;
}
```

### 3. Leverage Configuration in Tests

Use configuration files for test data and connection strings:

```json
// appsettings.Test.json
{
  "ConnectionStrings": {
    "TestDatabase": "Server=(localdb)\\mssqllocaldb;Database=TestDb;Trusted_Connection=True"
  },
  "TestSettings": {
    "TimeoutSeconds": 30,
    "MockExternalApis": true
  }
}
```

```csharp
protected override void ConfigureServices(IServiceCollection services, IConfiguration configuration)
{
    var testSettings = configuration.GetSection("TestSettings");
    services.Configure<TestSettings>(testSettings);
}
```

### 4. Use Null-Safe Collection Checks

Replace verbose null and empty checks with `IsNullOrEmpty()`:

```csharp
// ❌ Verbose
if (items == null || !items.Any())
{
    return;
}

// ✅ Concise
if (items.IsNullOrEmpty())
{
    return;
}
```

### 5. Clean Up Resources Properly

Override `OnCleanup` for proper resource disposal:

```csharp
protected override void OnCleanup()
{
    // Clean up test data
    _testDatabase?.Dispose();
    _tempFiles?.ForEach(File.Delete);
}
```

## Thread Safety

- ✅ **InvocationTracker**: Thread-safe using `ConcurrentDictionary`
- ✅ **AlreadyInvoked**: Thread-safe, can be called concurrently
- ⚠️ **KdyfTestBase**: Not thread-safe, each test should have its own instance (handled by MSTest)

## Requirements

- .NET 8.0 or higher
- Microsoft.Extensions.DependencyInjection.Abstractions 8.0.2+
- MSTest framework (for KdyfTestBase)

## Related Packages

- **kdyf.Operations**: Operations orchestration framework
- **kdyf.Notifications**: Reactive notification system
- **kdyf.Notifications.Redis**: Redis transport for notifications

## License

This project is licensed under the MIT License - see the LICENSE file for details.

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## Support

For issues, questions, or contributions, please visit the [GitHub repository](https://github.com/kreadyf/kdyf.common).

---

Made with ❤️ by Kreadyf SRL
