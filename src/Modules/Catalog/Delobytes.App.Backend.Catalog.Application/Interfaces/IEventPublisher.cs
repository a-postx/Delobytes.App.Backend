namespace Delobytes.App.Backend.Catalog.Application.Interfaces;

/// <summary>
/// Publishes Catalog domain events to the message bus. Kept as an Application-layer
/// abstraction (mirrors the Integrations module) so handlers do not depend on the MassTransit
/// package directly.
/// </summary>
public interface IEventPublisher
{
    /// <summary>
    /// Publishes an event to the message bus.
    /// </summary>
    /// <typeparam name="TEvent">Event type.</typeparam>
    /// <param name="event">Event instance.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken) where TEvent : class;
}
