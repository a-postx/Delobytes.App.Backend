namespace Delobytes.App.Backend.Catalog.Application.Queries.ChannelParameterSets.GetChannelParameterSets;

public class GetChannelParameterSetsResponse
{
    public List<ChannelParameterSetDto> Items { get; set; } = new List<ChannelParameterSetDto>();
}

public class ChannelParameterSetDto
{
    public Guid Id { get; set; }
    public Guid ChannelId { get; set; }
    public DateOnly ValidFrom { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
