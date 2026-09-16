using Delobytes.App.Backend.Contracts.Interfaces;

namespace Delobytes.App.Backend.Catalog.Domain.Entities;

/// <summary>
/// Represents one packed unit of a product as it is shipped to a sales channel.
/// Holds the outer dimensions and gross weight that marketplace volume tariffs
/// and carrier APIs are calculated from.
/// Packaging material is a separate <see cref="Component"/>: one purchased roll
/// or box serves many shipped units, so dimensions cannot live on the component.
/// </summary>
public class PackingUnit : ITenantScoped
{
    public Guid Id { get; set; }

    public Guid ProductId { get; set; }

    /// <summary>Sales channel this unit is packed for; null means every channel. Not used yet.</summary>
    public Guid? ChannelId { get; set; }

    /// <summary>Optional label shown to the operator when a product has more than one packing.</summary>
    public string? Name { get; set; }

    /// <summary>Outer dimensions of the packed unit in cm; the tariff volume is derived from these.</summary>
    public decimal LengthCm { get; set; }

    public decimal WidthCm { get; set; }

    public decimal HeightCm { get; set; }

    /// <summary>Gross weight of the packed unit in kg, packaging material included.</summary>
    public decimal? WeightKg { get; set; }

    public bool IsActive { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Product Product { get; set; } = default!;

    /// <summary>Volume in litres used by marketplace volume-threshold tariffs.</summary>
    public decimal GetVolumeLiters()
    {
        decimal volume = LengthCm * WidthCm * HeightCm / 1000m;

        return Math.Round(volume, 3, MidpointRounding.AwayFromZero);
    }
}
