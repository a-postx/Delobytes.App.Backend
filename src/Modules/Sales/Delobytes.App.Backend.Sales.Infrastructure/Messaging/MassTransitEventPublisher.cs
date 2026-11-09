using Delobytes.App.Backend.Sales.Application.Interfaces;
using MassTransit;

namespace Delobytes.App.Backend.Sales.Infrastructure.Messaging;

/// <summary>
/// MassTransit-based implementation of IEventPublisher. Mirrors
/// Catalog.Infrastructure.Messaging.MassTransitEventPublisher.
/// </summary>
public class MassTransitEventPublisher : IEventPublisher
{
    private readonly IPublishEndpoint _publishEndpoint;

    /// <summary>
    /// Initializes a new instance of the <see cref="MassTransitEventPublisher"/> class.
    /// </summary>
    /// <param name="publishEndpoint">MassTransit publish endpoint.</param>
    public MassTransitEventPublisher(IPublishEndpoint publishEndpoint)
    {
        _publishEndpoint = publishEndpoint;
    }

    /// <inheritdoc/>
    public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken) where TEvent : class
    {
        await _publishEndpoint.Publish(@event, cancellationToken);
    }
}
