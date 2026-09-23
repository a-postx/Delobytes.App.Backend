namespace Delobytes.App.Backend.Integrations.Application.DTOs.Channels;

/// <summary>
/// A system channel template — pure catalog of supported marketplace integrations
/// (Wildberries, Ozon, etc.). Carries no connection/account state: a template can be used
/// by any number of Catalog.Channel + Connection pairs.
/// </summary>
public class AvailableChannelDto
{
    public Guid Id { get; set; }

    public string Code { get; set; } = default!;

    public string DisplayName { get; set; } = default!;

    public string? Description { get; set; }

    public string ApiVersion { get; set; } = default!;
}
