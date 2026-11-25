using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using Delobytes.App.Backend.Composition;
using Delobytes.App.Backend.Infrastructure;
using Serilog;

namespace Delobytes.App.Backend;

/// <summary>
/// Application entry point and composition.
/// </summary>
public partial class Program
{
    internal static readonly string? AppName = Assembly.GetEntryAssembly()?.GetName().Name;
    internal static readonly Version? AppVersion = Assembly.GetEntryAssembly()?.GetName().Version;
    internal static readonly string? RootPath = Path.GetDirectoryName(typeof(Program).Assembly.Location);

    /// <summary>
    /// Entry point.
    /// </summary>
    /// <param name="args">Command line arguments.</param>
    /// <returns>Exit code.</returns>
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Activity.DefaultIdFormat = ActivityIdFormat.W3C;

        // Bootstrap logger (console only) before full Serilog configuration is available.
        // This captures startup errors in case YC Lockbox or config loading fails.
        Log.Logger = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
            .CreateBootstrapLogger();

        try
        {
            await using WebApplication app = await CreateApp(args);

            await app.RunAsync();

            return 0;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application terminated unexpectedly.");
            return 1;
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }

    /// <summary>Builds the application.</summary>
    /// <param name="args">Command line arguments.</param>
    /// <param name="configure">Optional callback applied right after the builder is created.</param>
    /// <param name="configureServices">
    /// Optional callback applied after every module registered its services. This is the only point
    /// where a test can replace a module registration (for example its DbContext provider) and win.
    /// </param>
    /// <returns>The composed application, ready to run.</returns>
    public static async Task<WebApplication> CreateApp(
        string[] args,
        Action<WebApplicationBuilder>? configure = null,
        Action<IServiceCollection>? configureServices = null)
    {
        Log.Information("Building Web App...");

        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        // запускается перед чтением любых настроек, здесь тесты правят конфиг и подменяют сервер
        configure?.Invoke(builder);

        Log.Information("Hosting environment is {EnvironmentName}", builder.Environment.EnvironmentName);

        builder
            .AddHostConfiguration()
            .AddApiSurface()
            .AddObservability()
            .AddModules()
            .AddAppInfrastructure();

        configureServices?.Invoke(builder.Services);

        WebApplication app = builder.Build();

        IHostApplicationLifetime hostLifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
        hostLifetime.ApplicationStopping.Register(() =>
        {
            app.Services.GetRequiredService<ILogger<Program>>().LogInformation("Shutdown has been initiated.");
        });

        // Migrations and data backfills run on startup and must succeed: serving on an outdated
        // schema corrupts data silently, so a failure stops the host instead of retrying.
        if (builder.Configuration.GetValue("Database:MigrateOnStartup", true))
        {
            try
            {
                await app.Services.MigrateAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error applying database migration");
                throw;
            }
        }

        app.UseApiPipeline()
            .MapSystemEndpoints();

        if (builder.Configuration.GetValue("Background:Enabled", true))
        {
            // Publish test event to verify MassTransit + RabbitMQ connectivity on startup.
            await app.PublishStartedEventAsync();
        }

        return app;
    }
}
