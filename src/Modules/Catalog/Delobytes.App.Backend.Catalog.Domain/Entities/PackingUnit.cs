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

    /// <summary>
    /// Sales channel this unit describes. <c>null</c> marks the product's canonical unit — the
    /// dimensions the catalogue itself works with, including tariff volume. A non-null value is the
    /// unit that channel reports, kept per channel and never used as the canonical one.
    /// </summary>
    /// <remarks>
    /// At most one row per <c>(ProductId, ChannelId)</c> and at most one canonical row per product;
    /// both bounds are enforced by unique indexes. Neither is mandatory: a product may have no
    /// canonical unit at all, so every reader must accept <c>null</c> instead of dereferencing.
    /// Postgres treats <c>NULL</c> values as distinct, which is why the canonical bound needs a
    /// filtered index rather than this column being part of a plain unique index.
    /// </remarks>
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
