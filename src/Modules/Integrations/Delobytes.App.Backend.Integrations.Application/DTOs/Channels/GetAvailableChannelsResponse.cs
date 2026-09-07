namespace Delobytes.App.Backend.Integrations.Application.DTOs.Channels;

public class GetAvailableChannelsResponse
{
    public List<AvailableChannelDto> Items { get; set; } = new List<AvailableChannelDto>();
}
