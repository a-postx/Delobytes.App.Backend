using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Delobytes.App.Backend.Application.Behaviours;
using Delobytes.App.Backend.BackgroundServices;
using Delobytes.App.Backend.Catalog.Api;
using Delobytes.App.Backend.Constants;
using Delobytes.App.Backend.Extensions;
using Delobytes.App.Backend.Identity.Api;
using Delobytes.App.Backend.Infrastructure;
using Delobytes.App.Backend.Integrations.Api;
using Delobytes.App.Backend.Integrations.Application.Options;
using Delobytes.App.Backend.Messaging.Events;
using Delobytes.App.Backend.Middleware;
using Delobytes.App.Backend.Options;
using Delobytes.App.Backend.Sales.Api;
using Delobytes.AspNetCore.Common.Constants;
using Delobytes.AspNetCore.Logging;
using MassTransit;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.OpenApi.Models;
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

            builder.Services.Configure<HostOptions>(opts => opts.ShutdownTimeout = TimeSpan.FromSeconds(30));

            builder.WebHost
                .UseKestrel((builderContext, options) =>
                    {
                        options.AddServerHeader = false;
                        options.Configure(builderContext.Configuration.GetSection(nameof(AppSettings.Kestrel)));
                    })
                .CaptureStartupErrors(true)
                .UseShutdownTimeout(TimeSpan.FromSeconds(Timeouts.WebHostShutdownTimeoutSec));

            builder.Services.AddControllers()
                .AddJsonOptions(options =>
                {
                    options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
                    options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
                });

            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen(options =>
            {
                options.DescribeAllParametersInCamelCase();
                options.EnableAnnotations();

                options.SwaggerDoc("v1", new OpenApiInfo
                {
                    Title = "Delobytes App Backend API",
                    Version = "v1",
                    Description = "Delobytes e-commerce margin accounting system API",
                });

                options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
                {
                    Description = "JWT Authorization header using the Bearer scheme.",
                    Name = "Authorization",
                    In = ParameterLocation.Header,
                    Type = SecuritySchemeType.ApiKey,
                    Scheme = "Bearer"
                });

                options.AddSecurityRequirement(new OpenApiSecurityRequirement
                {
                    {
                        new OpenApiSecurityScheme
                        {
                            Reference = new OpenApiReference
                            {
                                Type = ReferenceType.SecurityScheme,
                                Id = "Bearer"
                            }
                        },
                        Array.Empty<string>()
                    }
                });
            });

            builder.Services.AddMediatR(cfg =>
            {
                cfg.AddIdentityHandlers();
                cfg.AddIntegrationsHandlers();
                cfg.AddCatalogHandlers();
                cfg.AddSalesHandlers();

                // Добавить Authorization Behaviour ПЕРЕД Validation Behaviour
                cfg.AddOpenBehavior(typeof(AuthorizationBehaviour<,>));
                cfg.AddOpenBehavior(typeof(ValidationBehaviour<,>));
            });

            builder.Services.AddIdentityApi();
            builder.Services.AddIntegrationsApi();
            builder.Services.AddCatalogApi();
            builder.Services.AddSalesApi();

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

                config.PrivateKey = Environment.GetEnvironmentVariable("YC_PRIVATE_KEY") ?? throw new InvalidOperationException("YC_PRIVATE_KEY is required.");
                config.ServiceAccountId = tempConfig.GetValue<string>("YC:ServiceAccountId") ?? throw new InvalidOperationException("YC:ServiceAccountId is required.");
                config.ServiceAccountAuthorizedKeyId = tempConfig.GetValue<string>("YC:ServiceAccountAuthorizedKeyId") ?? throw new InvalidOperationException("YC:ServiceAccountAuthorizedKeyId is required.");
                config.SecretId = tempConfig.GetValue<string>("YC:ConfigurationSecretId") ?? throw new InvalidOperationException("YC:ConfigurationSecretId is required.");
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

            // ── Serilog ─────────────────────────────────────────────────────────────
            builder.AddSerilog(secrets);
            builder.Services.AddHttpContextLogging(options =>
            {
                options.LogRequestBody = true;
                options.LogResponseBody = true;
                options.MaxBodyLength = 32759;
                options.SkipPaths = new List<PathString> { "/metrics" };
                options.SkipRequestHeaders = new List<string> { "Authorization" };
            });

            // ── Infrastructure (Http / EF Core / PostgreSQL) ───────────────────────────────
            builder.Services
                .AddInfrastructure(builder.Configuration, secrets);

            // ── Health checks ───────────────────────────────────────────────────────
            builder.Services.AddCustomHealthChecks(secrets);

            // ── MassTransit + RabbitMQ (CloudAMQP) ─────────────────────────────────-
            builder.Services.AddMessaging(secrets?.MessageBusConnectionString);

            // ── Background jobs ──────────────────
            // Separate IHostedServices: the scheduler only creates Pending JobRun rows per cron
            // schedule, the worker is the sole place that enforces per-tenant parallelism and
            // publishes the event that actually triggers the orchestration consumer.
            builder.Services.AddHostedService<SchedulerHostedService>();
            builder.Services.AddHostedService<WorkerHostedService>();
            builder.Services.AddHostedService<WatchdogHostedService>();
            builder.Services.AddHostedService<RawApiResponseCleanupHostedService>();

            // ── CORS ─────────────────────────────────────────────────────────────────────
            builder.Services.AddCustomCors();

            WebApplication app = builder.Build();

            IHostApplicationLifetime hostLifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
            hostLifetime.ApplicationStopping.Register(() =>
            {
                app.Services.GetRequiredService<ILogger<Program>>().LogInformation("Shutdown has been initiated.");
            });

            try
            {
                await app.Services.ApplyMigrationsAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error applying database migration");
                return 1;
            }

            app.UseMiddleware<CorrelationIdMiddleware>();

            app.UseHttpContextLogging();

            app.UseMiddleware<ExceptionHandlingMiddleware>();

            app.UseCors(CorsPolicyNames.AllowAny);

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
            app.UseAuthentication()
                .UseClaimsLogging();
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
