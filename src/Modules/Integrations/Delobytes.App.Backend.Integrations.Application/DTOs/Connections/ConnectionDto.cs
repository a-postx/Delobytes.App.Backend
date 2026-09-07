namespace Delobytes.App.Backend.Integrations.Application.DTOs.Connections;

public class ConnectionDto
{
    public Guid Id { get; set; }
    public string ChannelCode { get; set; } = default!;
    public string ChannelDisplayName { get; set; } = default!;
    public bool IsActive { get; set; }
    public DateTimeOffset? LastSyncAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
