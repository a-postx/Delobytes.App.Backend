namespace Delobytes.App.Backend.Integrations.Application.DTOs.Channels;

public class AvailableChannelDto
{
    public string Code { get; set; } = default!;
    public string DisplayName { get; set; } = default!;
    public string? Description { get; set; }
    public string ApiVersion { get; set; } = default!;
    public bool IsConnected { get; set; }
    // Populated only when IsConnected = true
    public Guid? ConnectionId { get; set; }
    public string? MaskedApiKey { get; set; }
    public string? CustomerName { get; set; }
    public string? LegalName { get; set; }
    public string? Inn { get; set; }
}
