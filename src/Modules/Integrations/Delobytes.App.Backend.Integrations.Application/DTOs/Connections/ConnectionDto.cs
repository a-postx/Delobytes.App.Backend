namespace Delobytes.App.Backend.Integrations.Application.DTOs.Connections;

public class ConnectionDto
{
    public Guid Id { get; set; }

    /// <summary>Identifier of the Catalog.Channel this connection is linked to.</summary>
    public Guid ChannelId { get; set; }

    public string TemplateCode { get; set; } = default!;

    public string TemplateDisplayName { get; set; } = default!;

    public bool IsActive { get; set; }

    public DateTimeOffset? LastSyncAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public string? MaskedApiKey { get; set; }

    public string? CustomerName { get; set; }

    public string? CustomerLegalName { get; set; }

    public string? CustomerInn { get; set; }
}
