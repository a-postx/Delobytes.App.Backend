namespace Delobytes.App.Backend.Catalog.Application.Queries.Channels.GetChannels;

public class ChannelDto
{
    public Guid Id { get; set; }

    public string Name { get; set; } = default!;

    public Guid? SystemChannelTemplateId { get; set; }

    /// <summary>System channel template code ("wildberries", "ozon", "yandex.kit"), or null for a custom channel.</summary>
    public string? Code { get; set; }

    public bool IsCustom { get; set; }

    public bool IsActive { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }
}

public class GetChannelsResponse
{
    public List<ChannelDto> Items { get; set; } = new List<ChannelDto>();
}
