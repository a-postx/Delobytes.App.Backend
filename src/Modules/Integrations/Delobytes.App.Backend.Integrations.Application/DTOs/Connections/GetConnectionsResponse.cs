namespace Delobytes.App.Backend.Integrations.Application.DTOs.Connections;

public class GetConnectionsResponse
{
    public List<ConnectionDto> Items { get; set; } = new List<ConnectionDto>();
}
