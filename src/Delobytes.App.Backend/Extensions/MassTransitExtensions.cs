using Delobytes.App.Backend.Messaging.Consumers;
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
            // Register all consumers
            bus.AddConsumer<AppStartedEventConsumer>();

            if (!string.IsNullOrWhiteSpace(messageBusConnectionString))
            {
                // Production: RabbitMQ via CloudAMQP
                bus.UsingRabbitMq((ctx, cfg) =>
                {
                    cfg.Host(new Uri(messageBusConnectionString));
                    cfg.ConfigureEndpoints(ctx);
                });
            }
            else
            {
                // Development fallback: in-memory transport (no external dependencies)
                bus.UsingInMemory((ctx, cfg) =>
                    {
                        cfg.ConfigureEndpoints(ctx);
                    });
            }
        });

        return services;
    }
}
