using Delobytes.App.Backend.Catalog.Infrastructure;
using Delobytes.App.Backend.Catalog.Infrastructure.Messaging.Consumers;
using Delobytes.App.Backend.Filters;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using Delobytes.App.Backend.Integrations.Infrastructure;
using Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers;
using Delobytes.App.Backend.Messaging.Consumers;
using Delobytes.App.Backend.Sales.Infrastructure;
using Delobytes.App.Backend.Services;
using MassTransit;

namespace Delobytes.App.Backend.Extensions;

/// <summary>
/// Extension methods for configuring MassTransit message bus infrastructure.
/// </summary>
internal static class MassTransitExtensions
{
    /// <summary>
    /// Registers MassTransit with RabbitMQ transport (CloudAMQP).
    /// If the CloudAMQP connection string is not provided, MassTransit is registered
    /// with the in-memory transport — useful for local development without CloudAMQP.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="messageBusConnectionString">Message bus (CloudAMQP) connection string. May be null in development.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddMessaging(this IServiceCollection services, string? messageBusConnectionString)
    {
        services.AddMassTransit(bus =>
        {
            bus.AddConsumer<AppStartedEventConsumer>();
            bus.AddIntegrationsConsumers();
            bus.AddCatalogConsumers();
            bus.AddSalesConsumers();

            if (!string.IsNullOrWhiteSpace(messageBusConnectionString))
            {
                bus.UsingRabbitMq((ctx, cfg) =>
                {
                    cfg.Host(new Uri(messageBusConnectionString));

                    ConfigureCommonFilters(cfg, ctx);
                    ConfigureSharedEndpoints(cfg, ctx);

                    // Bus-wide retry baseline so RabbitMQ consumers without an endpoint-specific
                    // UseMessageRetry override still get the same resilience as the InMemory transport.
                    cfg.UseMessageRetry(r => r.Intervals(100, 500, 1000));

                    cfg.ConfigureEndpoints(ctx);
                });
            }
            else
            {
                bus.UsingInMemory((ctx, cfg) =>
                {
                    ConfigureCommonFilters(cfg, ctx);
                    ConfigureSharedEndpoints(cfg, ctx);

                    // Configure product batch import endpoint
                    cfg.ReceiveEndpoint("catalog-products-import", e =>
                    {
                        e.ConfigureConsumer<ImportProductBatchMassTransitConsumer>(ctx);

                        // Retry policy with exponential backoff
                        e.UseMessageRetry(r => r.Exponential(5, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5)));

                        // Higher concurrency for batch processing
                        e.UseConcurrencyLimit(5);
                    });

                    cfg.ConfigureEndpoints(ctx);
                });
            }
        });

        return services;
    }

    // Shared between RabbitMQ and InMemory transports so both branches enforce the same
    // tenant/correlation/user propagation instead of only the one a developer remembered to edit.
    private static void ConfigureCommonFilters(IBusFactoryConfigurator cfg, IBusRegistrationContext ctx)
    {
        // Register tenant filters for all messages
        cfg.UsePublishFilter(typeof(TenantPublishFilter<>), ctx);
        cfg.UseConsumeFilter(typeof(TenantConsumeFilter<>), ctx);

        // Register correlation filters for all messages
        cfg.UsePublishFilter(typeof(CorrelationPublishFilter<>), ctx);
        cfg.UseConsumeFilter(typeof(CorrelationConsumeFilter<>), ctx);

        // Register user filters for all messages, so a consumer can attribute its writes
        // to the user who started the flow even for background-initiated jobs.
        cfg.UsePublishFilter(typeof(UserPublishFilter<>), ctx);
        cfg.UseConsumeFilter(typeof(UserConsumeFilter<>), ctx);
    }

    // Shared between RabbitMQ and InMemory transports: these endpoints previously existed only
    // in the InMemory branch, so RabbitMQ silently ran with default concurrency/retry instead.
    private static void ConfigureSharedEndpoints(IBusFactoryConfigurator cfg, IBusRegistrationContext ctx)
    {
        // Configure products import orchestration endpoint
        cfg.ReceiveEndpoint("integrations-products-import", e =>
        {
            e.ConfigureConsumer<ProcessProductsImportMassTransitConsumer>(ctx);

            // Retry policy with exponential backoff
            e.UseMessageRetry(r => r.Exponential(5, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5)));

            // Concurrency limit for cursor-based pagination
            e.UseConcurrencyLimit(1);
        });

        // Configure product import batch completion endpoint
        cfg.ReceiveEndpoint("integrations-products-import-results", e =>
        {
            e.ConfigureConsumer<ProcessProductImportBatchCompletedMassTransitConsumer>(ctx);

            // Retry policy with exponential backoff
            e.UseMessageRetry(r => r.Exponential(5, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5)));
        });
    }
}
