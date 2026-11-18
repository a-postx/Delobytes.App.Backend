using Delobytes.App.Backend.Finance.Application.Interfaces;
using MassTransit;

namespace Delobytes.App.Backend.Finance.Infrastructure.Messaging;

/// <summary>
/// MassTransit-based implementation of <see cref="IEventPublisher"/>.
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
