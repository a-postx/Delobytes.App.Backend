using Delobytes.App.Backend.Contracts.Interfaces;

namespace Delobytes.App.Backend.Catalog.Domain.Entities;

/// <summary>
/// A single threshold/rate row inside a TariffGrid.
/// For WB: RegionOrCity is the region name, VolumeThresholdLiters is null.
/// For fulfilment-centre: RegionOrCity is the city, VolumeThresholdLiters is the upper volume boundary.
/// </summary>
public class TariffGridEntry : ITenantScoped
{
    public Guid Id { get; set; }

    public Guid TariffGridId { get; set; }

    public string RegionOrCity { get; set; } = default!;

    /// <summary>Upper volume boundary in litres for this bracket. Null means "above all previous thresholds".</summary>
    public decimal? VolumeThresholdLiters { get; set; }

    /// <summary>Tariff rate in currency units (per box for WB logistics, per litre for FF-centre).</summary>
    public decimal Rate { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public TariffGrid TariffGrid { get; set; } = default!;
}
