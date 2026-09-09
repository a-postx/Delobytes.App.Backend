using Delobytes.App.Backend.Catalog.Domain.Enums;

namespace Delobytes.App.Backend.Catalog.Application.Queries.TariffGrids.GetTariffGrid;

public class GetTariffGridResponse
{
    public Guid Id { get; set; }

    public string Name { get; set; } = default!;

    public TariffType TariffType { get; set; }

    public DateOnly ValidFrom { get; set; }

    public Guid? ChannelId { get; set; }

    public bool IsActive { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public IReadOnlyList<TariffGridEntryResponse> Entries { get; set; } = new List<TariffGridEntryResponse>();
}

public class TariffGridEntryResponse
{
    public Guid Id { get; set; }

    public string RegionOrCity { get; set; } = default!;

    public decimal? VolumeThresholdLiters { get; set; }

    public decimal Rate { get; set; }
}
