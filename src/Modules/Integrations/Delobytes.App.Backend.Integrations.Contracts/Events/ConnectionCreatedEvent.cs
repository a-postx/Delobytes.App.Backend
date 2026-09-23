namespace Delobytes.App.Backend.Integrations.Contracts.Events;

/// <summary>
/// Published by Integrations when a new connection is successfully created and validated.
/// Reserved for future background synchronization triggers (e.g. initial data import job).
/// Catalog module does NOT subscribe to this event — Channel lifecycle is fully independent
/// of Connection lifecycle by design.
/// </summary>
public record ConnectionCreatedEvent
{
    public Guid ConnectionId { get; init; }

    /// <summary>Identifier of the Catalog.Channel this connection is linked to.</summary>
    public Guid ChannelId { get; init; }

    public Guid SystemChannelTemplateId { get; init; }
}
