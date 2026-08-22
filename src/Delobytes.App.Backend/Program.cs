using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Delobytes.App.Backend.Extensions;
using Delobytes.App.Backend.Infrastructure;
using Delobytes.App.Backend.Messaging.Events;
using Delobytes.App.Backend.Options;
using Delobytes.Extensions.Configuration;
using FluentValidation;
using MassTransit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Serilog;

namespace Delobytes.App.Backend;

/// <summary>
/// Main program.
/// </summary>
public partial class Program
{
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
            .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .CreateBootstrapLogger();

        try
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

            builder.WebHost.UseKestrel((builderContext, options) =>
            {
                options.AddServerHeader = false;
                options.Configure(builderContext.Configuration.GetSection(nameof(AppSettings.Kestrel)));
            });

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

            ServiceProvider sp = builder.Services.BuildServiceProvider();
            AppSecrets? secrets = sp.GetService<IOptions<AppSecrets>>()?.Value;
            Auth0Options auth0Options = sp.GetRequiredService<IOptions<Auth0Options>>().Value;

            // ── Serilog ─────────────────────────────────────────────────────────────
            // Logs go EXCLUSIVELY to Grafana Cloud Loki (per tech stack, section 5.1).
            // Console sink is enabled only in Development for local diagnostics.
            // File sink is intentionally NOT configured.
            builder.AddSerilog(secrets);

            // ── Infrastructure (EF Core / PostgreSQL) ───────────────────────────────
            builder.Services
                .AddInfrastructure(builder.Configuration, secrets?.ConnectionString);

            // ── Health checks ───────────────────────────────────────────────────────
            IHealthChecksBuilder healthChecksBuilder = builder.Services.AddHealthChecks();

            if (secrets != null && secrets.ConnectionString != null)
            {
                healthChecksBuilder.AddNpgSql(
                    secrets.ConnectionString,
                    "SELECT 1;",
                    null,
                    "Database",
                    HealthStatus.Unhealthy,
                    new string[] { "ready", "metric", "db", "sql", "postgresql" });
            }

            // ── MassTransit + RabbitMQ (CloudAMQP) ─────────────────────────────────
            // Connects to CloudAMQP when CloudAmqpConnectionString is set;
            // falls back to in-memory transport in Development without credentials.
            builder.Services.AddMessaging(secrets?.MessageBusConnectionString);

            // ── Auth0 JWT Bearer authentication ─────────────────────────────────────
            // Auth0 is responsible ONLY for user identity verification (authentication).
            // Authorization (roles, tenants) is implemented in the application layer.
            builder.Services.AddAuth0Authentication(auth0Options);

            // ── CORS ────────────────────────────────────────────────────────────────
            builder.Services.AddCustomCors();

            WebApplication app = builder.Build();

            IHostApplicationLifetime hostLifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
            hostLifetime.ApplicationStopping.Register(() =>
            {
                app.Services.GetRequiredService<ILogger<Program>>().LogInformation("Shutdown has been initiated.");
            });

            await app.Services.ApplyMigrationsAsync();

            // Publish test event to verify MassTransit + RabbitMQ connectivity on startup
            IPublishEndpoint publishEndpoint = app.Services.GetRequiredService<IPublishEndpoint>();

            string appVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";

            await publishEndpoint.Publish(new AppStartedEvent
            {
                StartedAt = DateTimeOffset.UtcNow,
                ApplicationVersion = appVersion,
            });

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
                    JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web);
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
