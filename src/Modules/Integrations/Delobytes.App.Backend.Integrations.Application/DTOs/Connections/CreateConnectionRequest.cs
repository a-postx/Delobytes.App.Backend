namespace Delobytes.App.Backend.Integrations.Application.DTOs.Connections;

public class CreateConnectionRequest
{
    public Guid ChannelId { get; set; }
    public string SystemChannelTemplateCode { get; set; } = default!;
    public string ApiKey { get; set; } = default!;
    public string? ApiSecret { get; set; }
    public Dictionary<string, string>? Settings { get; set; }
}
