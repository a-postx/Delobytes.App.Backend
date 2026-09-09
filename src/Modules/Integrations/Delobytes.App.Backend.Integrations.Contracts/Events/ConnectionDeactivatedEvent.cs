namespace Delobytes.App.Backend.Integrations.Contracts.Events;

/// <summary>
/// Published by Integrations when a connection is deactivated.
/// </summary>
public record ConnectionDeactivatedEvent
{
    public Guid ChannelId { get; init; }
}
