using System.Reflection;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients;
using Delobytes.App.Backend.Integrations.Infrastructure.Messaging;
using Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers;
using Delobytes.App.Backend.Integrations.Infrastructure.Persistence;
using Delobytes.App.Backend.Integrations.Infrastructure.Persistence.Repositories;
using Delobytes.App.Backend.Integrations.Infrastructure.Policies;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Polly;

namespace Delobytes.App.Backend.Integrations.Infrastructure;

/// <summary>
/// Registers Integrations module infrastructure services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Integrations infrastructure services to the DI container.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="connectionString">Connection string.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddIntegrationsInfrastructure(this IServiceCollection services, string? connectionString)
    {
        if (connectionString == null)
        {
            throw new InvalidOperationException("Connection string is not configured.");
        }

        services.AddDbContext<IntegrationsDbContext>(options =>
            options.UseNpgsql(connectionString, npgsqlOptions =>
                npgsqlOptions.MigrationsHistoryTable("__IntegrationsMigrationsHistory", "integrations")));

        services.AddScoped<IConnectionRepository, ConnectionRepository>();
        services.AddScoped<ISyncJobRepository, SyncJobRepository>();
        services.AddScoped<IRawApiResponseRepository, RawApiResponseRepository>();
        services.AddScoped<IEventPublisher, MassTransitEventPublisher>();

        services.AddScoped<ProcessSyncJobConsumer>();

        services.AddHttpClient<WildberriesApiClient>()
            .AddResilienceHandler("retry-policy", (builder, context) =>
            {
                ILogger logger = context.ServiceProvider.GetRequiredService<ILogger<WildberriesApiClient>>();
                ResiliencePipeline<HttpResponseMessage> retryPipeline = RetryPolicies.GetRetryPolicy(logger);
                builder.AddPipeline(retryPipeline);
            });

        services.AddTransient<IChannelApiClientFactory, ChannelApiClientFactory>();

        return services;
    }

    /// <summary>
    /// Registers Integrations module MassTransit consumers.
    /// </summary>
    /// <param name="configurator">MassTransit bus registration configurator.</param>
    public static void AddIntegrationsConsumers(this IBusRegistrationConfigurator configurator)
    {
        configurator.AddConsumers(Assembly.GetExecutingAssembly());
    }
}
