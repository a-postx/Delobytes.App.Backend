using Delobytes.App.Backend.Constants;
using Delobytes.App.Backend.Identity.Application.Options;
using Delobytes.App.Backend.Infrastructure;
using Delobytes.App.Backend.Infrastructure.Health;
using Delobytes.App.Backend.Infrastructure.Health.System;
using Delobytes.App.Backend.Options;
using Delobytes.App.Backend.Options.Validators;
using Delobytes.App.Backend.Services;
using Delobytes.AspNetCore.Common.Constants;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Delobytes.App.Backend.Extensions;

internal static class ServiceCollectionExtensions
{
    /// <summary>
    /// Add cross-origin resource sharing (CORS) services and configures named CORS policies. See
    /// https://docs.asp.net/en/latest/security/cors.html
    /// </summary>
    public static IServiceCollection AddCustomCors(this IServiceCollection services)
    {
        return services.AddCors(options =>
            {
                // Create named CORS policies here which you can consume using application.UseCors("PolicyName")
                // or a [EnableCors("PolicyName")] attribute on your controller or action.
                options.AddPolicy(
                    CorsPolicyNames.AllowAny,
                    x => x
                        .AllowAnyOrigin()
                        .AllowAnyMethod()
                        .AllowAnyHeader()

                        // Without this the browser hides the correlation header from JavaScript,
                        // because the frontend and the API are served from different origins.
                        .WithExposedHeaders(CorrelationHeaders.CorrelationId));
            });
    }

    /// <summary>
    /// Configures the settings by binding the sources to the specified Plain Old CLR
    /// Objects (POCO) and adding <see cref="IOptions{T}"/> objects to the services collection.
    /// </summary>
    public static IServiceCollection AddCustomOptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<AppSecrets>, AppSecretsValidator>();

        // binds to Yandex Lockbox configuration source
        services
            .Configure<AppSecrets>(configuration.GetSection(nameof(AppSecrets)), o => o.BindNonPublicProperties = false);

        services.Configure<MultitenancyOptions>(
            configuration.GetSection(nameof(MultitenancyOptions)),
            o => o.BindNonPublicProperties = false);

        // JWT token validator — stateless, singleton-safe
        services.AddSingleton<JwtTokenValidator>();

        return services;
    }

    /// <summary>
    /// Создаёт экземпляры настроек, чтобы запустились валидаторы и проверили значения.
    /// Используем вместо встроенного процесса валидации ValidateOnStart т.к. он вызывается и
    /// проверяет только в процессе RunAsync.
    /// </summary>
    public static IServiceCollection ValidateRemoteSecrets(this IServiceCollection services)
    {
        try
        {
            ServiceProvider provider = services.BuildServiceProvider();
            AppSecrets? appSecrets = provider.GetService<IOptions<AppSecrets>>()?.Value;
        }
        catch (OptionsValidationException ex)
        {
            Console.WriteLine($"Error validating {ex.OptionsType.FullName}: {string.Join(", ", ex.Failures)}");
            throw;
        }

        return services;
    }

    public static IServiceCollection AddCustomHealthChecks(this IServiceCollection services, AppSecrets? secrets)
    {
        IHealthChecksBuilder builder = services.AddHealthChecks();

        builder.AddGenericHealthCheck<UptimeHealthCheck>("uptime")
                .AddMemoryHealthCheck(HealthCheckNames.Memory);

        if (secrets?.ConnectionString != null)
        {
            builder.AddNpgSql(
                secrets.ConnectionString,
                "SELECT 1;",
                null,
                HealthCheckNames.Database,
                HealthStatus.Unhealthy,
                new string[] { "ready", "metric", "db", "sql", "postgresql" });
        }

        ////services
        //// проверка дублирует встроенную проверку масстранзита
        //////.AddSingleton<MessageBusServiceHealthCheck>()
        ////.AddHealthChecks()
        //// проверка дублирует встроенную проверку масстранзита
        //////.AddGenericHealthCheck<MessageBusServiceHealthCheck>(HealthCheckNames.MessageBus, HealthStatus.Degraded, new[] { "ready", "metric" })
        //// Ping is not available on Azure Web Apps
        ////.AddNetworkHealthCheck("network");

        services.Configure<HealthCheckPublisherOptions>(options =>
        {
            options.Period = TimeSpan.FromSeconds(60);
            options.Timeout = TimeSpan.FromSeconds(30);
            options.Delay = TimeSpan.FromSeconds(15);
            ////options.Predicate = (check) => check.Tags.Contains("ready");
        });

        services.AddSingleton<IHealthCheckPublisher, MetricsPublisher>();
        services.AddSingleton<IHealthCheckPublisher, ReadinessPublisher>();

        return services;
    }
}
