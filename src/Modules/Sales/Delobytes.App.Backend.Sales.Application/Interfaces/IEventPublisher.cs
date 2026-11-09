namespace Delobytes.App.Backend.Sales.Application.Interfaces;

/// <summary>
/// Publishes Sales domain events to the message bus. Kept as an Application-layer abstraction
/// (mirrors the Catalog and Integrations modules) so handlers do not depend on MassTransit directly.
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
