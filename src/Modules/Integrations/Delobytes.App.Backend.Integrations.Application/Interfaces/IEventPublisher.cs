namespace Delobytes.App.Backend.Integrations.Application.Interfaces;

/// <summary>
/// Service for publishing integration events.
/// </summary>
public interface IEventPublisher
{
    /// <summary>
    /// Publishes an event to the message bus.
    /// </summary>
    /// <typeparam name="TEvent">Event type.</typeparam>
    /// <param name="event">Event instance.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken) where TEvent : class;
}
