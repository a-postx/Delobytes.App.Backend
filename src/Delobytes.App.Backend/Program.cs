using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Delobytes.App.Backend.Constants;
using Delobytes.App.Backend.Extensions;
using Delobytes.App.Backend.Infrastructure;
using Delobytes.App.Backend.Messaging.Events;
using Delobytes.App.Backend.Options;
using Delobytes.AspNetCore.Common.Constants;
using FluentValidation;
using MassTransit;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Serilog;

namespace Delobytes.App.Backend;

/// <summary>
/// Main program.
/// </summary>
public partial class Program
{
    internal static readonly string? AppName = Assembly.GetEntryAssembly()?.GetName().Name;
    internal static readonly Version? AppVersion = Assembly.GetEntryAssembly()?.GetName().Version;
    internal static readonly string? RootPath = Path.GetDirectoryName(typeof(Program).Assembly.Location);

    /// <summary>
    /// Entry point.
    /// </summary>
    /// <param name="args">Comand line arguments.</param>
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
            Log.Information("Building Web App...");

            WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

            Log.Information("Hosting environment is {EnvironmentName}", builder.Environment.EnvironmentName);

            builder.ConfigureJsonOptions();

            builder.WebHost
                .UseKestrel((builderContext, options) =>
                    {
                        options.AddServerHeader = false;
                        options.Configure(builderContext.Configuration.GetSection(nameof(AppSettings.Kestrel)));
                    })
                .CaptureStartupErrors(true)
                .UseShutdownTimeout(TimeSpan.FromSeconds(Timeouts.WebHostShutdownTimeoutSec));

            builder.Services.AddControllers();

            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen(options =>
            {
                options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
                {
                    Title = "Delobytes App Backend API",
                    Version = "v1",
                    Description = "Delobytes e-commerce margin accounting system API",
                });
            });

            builder.Services.AddMediatR(cfg =>
            {
                cfg.RegisterServicesFromAssembly(typeof(Program).Assembly);
                cfg.AddOpenBehavior(typeof(Application.Behaviours.ValidationBehaviour<,>));
            });

            builder.Services.AddValidatorsFromAssembly(typeof(Program).Assembly);

            builder.Configuration.AddYandexCloudLockboxConfiguration(config =>
            {
                builder.Configuration
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: false, reloadOnChange: true)
                    .AddEnvironmentVariables();

                IConfigurationRoot tempConfig = new ConfigurationBuilder()
                       .AddJsonFile("appsettings.json")
                       .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: false, reloadOnChange: false)
                       .Build();

                config.PrivateKey = Environment.GetEnvironmentVariable("YC_PRIVATE_KEY");
                config.ServiceAccountId = tempConfig.GetValue<string>("YC:ServiceAccountId");
                config.ServiceAccountAuthorizedKeyId = tempConfig.GetValue<string>("YC:ServiceAccountAuthorizedKeyId");
                config.SecretId = tempConfig.GetValue<string>("YC:ConfigurationSecretId");
                config.PathSeparator = '-';
                config.Optional = false;
                config.ReloadPeriod = TimeSpan.FromDays(7);
                config.LoadTimeout = TimeSpan.FromSeconds(20);
                config.OnLoadException += exceptionContext =>
                {
                    Log.Warning(exceptionContext.Exception, "Failed to load secrets from Yandex Cloud Lockbox: {Message}", exceptionContext.Exception.Message);
                };
            });

            builder.AddOptionsWithValidation();
            AppSecrets? secrets = builder.Configuration.GetSection(nameof(AppSecrets)).Get<AppSecrets>();
            ////Auth0Options? opty = builder.Configuration.GetSection(nameof(Auth0Options)).Get<Auth0Options>();

            // ── Serilog ─────────────────────────────────────────────────────────────
            builder.AddSerilog(secrets);

            // ── Infrastructure (EF Core / PostgreSQL) ───────────────────────────────
            builder.Services
                .AddInfrastructure(builder.Configuration, secrets?.ConnectionString);

            // ── Health checks ───────────────────────────────────────────────────────
            builder.Services.AddCustomHealthChecks(secrets);

            // ── MassTransit + RabbitMQ (CloudAMQP) ─────────────────────────────────
            // Connects to CloudAMQP when CloudAmqpConnectionString is set;
            // falls back to in-memory transport in Development without credentials.
            builder.Services.AddMessaging(secrets?.MessageBusConnectionString);

            ////builder.Services.AddAuth0Authentication(auth0Options);

            // ── CORS ────────────────────────────────────────────────────────────────
            builder.Services.AddCustomCors();

            WebApplication app = builder.Build();

            app.UseCors(CorsPolicyNames.AllowAny);

            IHostApplicationLifetime hostLifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
            hostLifetime.ApplicationStopping.Register(() =>
            {
                app.Services.GetRequiredService<ILogger<Program>>().LogInformation("Shutdown has been initiated.");
            });

            await app.Services.ApplyMigrationsAsync();

            // Publish test event to verify MassTransit + RabbitMQ connectivity on startup
            string appVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";
            using (IServiceScope publishScope = app.Services.CreateScope())
            {
                IPublishEndpoint publishEndpoint = publishScope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

                await publishEndpoint.Publish(new AppStartedEvent
                {
                    StartedAt = DateTimeOffset.UtcNow,
                    ApplicationVersion = appVersion,
                });
            }

            app.UseSwagger();
            app.UseSwaggerUI(options =>
            {
                options.SwaggerEndpoint("/swagger/v1/swagger.json", "Delobytes App Backend v1");
                options.RoutePrefix = "swagger";
            });

            // Authentication & authorization middleware must come before MapControllers
            app.UseAuthentication();
            app.UseAuthorization();

            app.MapGet("/status", () =>
            {
                return Results.Ok(new
                {
                    status = "ok",
                    version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0",
                    timestamp = DateTimeOffset.UtcNow,
                });
            })
                .WithName("Status")
                .WithTags("System")
                .Produces<object>(StatusCodes.Status200OK);

            app.MapHealthChecks("/metrics", new HealthCheckOptions
            {
                ResponseWriter = async (context, report) =>
                {
                    context.Response.ContentType = "application/json";
                    JsonSerializerOptions jsonOptions = new (JsonSerializerDefaults.Web);
                    string result = JsonSerializer.Serialize(
                        new
                        {
                            status = report.Status.ToString(),
                            totalDuration = report.TotalDuration,
                            entries = report.Entries.ToDictionary(
                                e => e.Key,
                                e => new
                                {
                                    status = e.Value.Status.ToString(),
                                    duration = e.Value.Duration,
                                    description = e.Value.Description,
                                }),
                        },
                        jsonOptions);
                    await context.Response.WriteAsync(result);
                },
            });

            app.MapControllers();

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
}
