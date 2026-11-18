namespace Delobytes.App.Backend.Finance.Application.Interfaces;

/// <summary>
/// Publishes Finance domain events to the message bus. Kept as an Application-layer abstraction
/// (mirrors Sales and Catalog) so handlers do not depend on MassTransit directly.
/// </summary>
public interface IEventPublisher
{
    /// <summary>
    /// Publishes an event to the message bus.
    /// </summary>
    /// <typeparam name="TEvent">Event type.</typeparam>
    /// <param name="event">Event instance.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the event is published.</returns>
    public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken) where TEvent : class;
}
