namespace Delobytes.App.Backend.Integrations.Contracts.Events;

/// <summary>
/// Published by Integrations when a new connection is successfully created.
/// </summary>
public record ConnectionCreatedEvent
{
    public Guid ChannelId { get; init; }
    public Guid? SystemChannelTemplateId { get; init; }
    public string ChannelName { get; init; } = default!;
}
