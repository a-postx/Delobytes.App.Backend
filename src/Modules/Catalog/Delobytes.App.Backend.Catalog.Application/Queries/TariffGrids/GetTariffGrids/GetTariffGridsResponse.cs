using Delobytes.App.Backend.Catalog.Domain.Enums;

namespace Delobytes.App.Backend.Catalog.Application.Queries.TariffGrids.GetTariffGrids;

public class GetTariffGridsResponse
{
    public IReadOnlyList<TariffGridItem> Items { get; set; } = new List<TariffGridItem>();
}

public class TariffGridItem
{
    public Guid Id { get; set; }

    public string Name { get; set; } = default!;

    public TariffType TariffType { get; set; }

    public DateOnly ValidFrom { get; set; }

    public Guid? ChannelId { get; set; }

    public bool IsActive { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
