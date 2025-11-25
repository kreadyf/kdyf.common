using kdyf.Notifications.Integration;
using kdyf.Notifications.Redis.Integration;
using kdyf.Notifications.Sample.Shared.Entities;
using kdyf.Notifications.Sample01.Console.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;

namespace kdyf.Notifications.Sample01.Console;

/// <summary>
/// Sample application demonstrating:
/// - Default Notification (InMemory)
/// - Redis Receiver listening to Consumer-Groups "random" and "webhook"
/// - Stream-only RandomNumber notifications (no Redis key created)
/// - Multiple non-blocking listeners in BackgroundServices
/// </summary>
class Program
{
    static async Task Main(string[] args)
    {
        // Verify Redis connection before starting
        if (!await VerifyRedisConnectionAsync())
        {
            System.Console.ForegroundColor = ConsoleColor.Red;
            System.Console.WriteLine("Cannot start application: Redis connection failed.");
            System.Console.ResetColor();
            System.Console.WriteLine("\nPress any key to exit...");
            System.Console.ReadKey();
            return;
        }

        System.Console.WriteLine("==========================================================");
        System.Console.WriteLine("   kdyf.Notifications.Sample01.Console");
        System.Console.WriteLine("==========================================================");
        System.Console.WriteLine("Features:");
        System.Console.WriteLine("  - Default Notification (InMemory)");
        System.Console.WriteLine("  - Redis Receiver with Consumer-Groups: 'random', 'webhook'");
        System.Console.WriteLine("  - Stream-only RandomNumber notifications");
        System.Console.WriteLine("  - Multiple non-blocking listeners in BackgroundServices");
        System.Console.WriteLine("==========================================================\n");

        var host = Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((hostingContext, config) =>
            {
                config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                config.AddUserSecrets<Program>();
            })
            .ConfigureServices((context, services) =>
            {
                services.AddKdyfNotification(context.Configuration)
                    // Configure Redis Target: RandomNumber goes to "notifications:stream:random" as stream-only (no key-value storage)
                    .AddRedisTarget(configure => configure
                        .WithStream("notifications:stream:default")
                        .WithStreamOnly<RandomNumberNotification>("notifications:stream:random"))
                    // Configure Redis Source: Listen to "random" and "webhook" streams
                    .AddRedisSource(configure => configure
                        .WithStreams("notifications:stream:random", "notifications:stream:webhook"))
                    // Configure Redis options for remote server with high latency
                    .ConfigureRedisOptions(opts =>
                    {
                        // Performance: Settings for remote Redis server (blocking mode)
                        opts.Performance.ChannelCapacity = 20000;
                        opts.Performance.XReadGroupBlockMs = 15000;  // 15s for high-latency network
                        opts.Performance.InitializationTimeoutMs = 15000;  // 15s timeout

                        // Resilience: Conservative settings for remote server
                        opts.Resilience.ErrorRecoveryDelayMs = 2000;  // 2s (reduced from 3s)
                        opts.Resilience.RetryDelayMs = 3000;  // 3s (reduced from 5s)
                    })
                    .Build();

                services.AddHostedService<RandomNumberEmissionService>();
                services.AddHostedService<RandomNumberBlockingListenerService>();
                services.AddHostedService<RandomNumberNonBlockingListenerService>();
                services.AddHostedService<OtherNotificationsListenerService>();
            })
            .Build();

        System.Console.WriteLine("✓ Services initialized\n");
        System.Console.WriteLine("Starting background services...\n");

        await host.RunAsync();
    }

    static async Task<bool> VerifyRedisConnectionAsync()
    {
        try
        {
            System.Console.ForegroundColor = ConsoleColor.Cyan;
            System.Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
            System.Console.WriteLine("║     kdyf.Notifications - Redis Connection Verification       ║");
            System.Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
            System.Console.ResetColor();
            System.Console.WriteLine();
            System.Console.Write("Checking Redis connection... ");

            // Load configuration from appsettings.json only
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddUserSecrets<Program>()
                .Build();

            var connectionString = configuration.GetSection("Redis")["ConnectionString"];

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                System.Console.ForegroundColor = ConsoleColor.Red;
                System.Console.WriteLine("FAILED");
                System.Console.ResetColor();
                System.Console.WriteLine("\nError: Redis:ConnectionString not configured.");
                System.Console.WriteLine("\nPlease configure Redis connection in appsettings.json:");
                System.Console.WriteLine("  \"Redis\": { \"ConnectionString\": \"localhost:6379\" }");
                return false;
            }

            // Attempt to connect to Redis
            var options = ConfigurationOptions.Parse(connectionString);
            options.AbortOnConnectFail = false;
            options.ConnectTimeout = 5000;

            using var redis = await ConnectionMultiplexer.ConnectAsync(options);

            if (!redis.IsConnected)
            {
                System.Console.ForegroundColor = ConsoleColor.Red;
                System.Console.WriteLine("FAILED");
                System.Console.ResetColor();
                System.Console.WriteLine($"\nError: Unable to connect to Redis at {connectionString}");
                System.Console.WriteLine("\nPlease verify:");
                System.Console.WriteLine("  • Redis server is running");
                System.Console.WriteLine("  • Connection string is correct");
                System.Console.WriteLine("  • Network/firewall settings allow the connection");
                return false;
            }

            // Verify we can execute commands
            var db = redis.GetDatabase();
            var pingResult = await db.PingAsync();

            System.Console.ForegroundColor = ConsoleColor.Green;
            System.Console.WriteLine("SUCCESS");
            System.Console.ResetColor();
            System.Console.WriteLine($"Connected to: {connectionString}");
            System.Console.WriteLine($"Ping latency: {pingResult.TotalMilliseconds:F2}ms");
            System.Console.WriteLine();
            System.Console.ForegroundColor = ConsoleColor.Green;
            System.Console.WriteLine("✓ Ready to start background services");
            System.Console.ResetColor();
            System.Console.WriteLine();

            return true;
        }
        catch (Exception ex)
        {
            System.Console.ForegroundColor = ConsoleColor.Red;
            System.Console.WriteLine("FAILED");
            System.Console.ResetColor();
            System.Console.WriteLine($"\nError: {ex.Message}");
            System.Console.WriteLine("\nPlease ensure:");
            System.Console.WriteLine("  • Redis server is running (redis-server)");
            System.Console.WriteLine("  • Connection string is configured");
            System.Console.WriteLine("  • Port is accessible (default: 6379)");
            return false;
        }
    }
}
