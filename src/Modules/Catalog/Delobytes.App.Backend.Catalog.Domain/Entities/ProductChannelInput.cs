using Delobytes.App.Backend.Contracts.Interfaces;

namespace Delobytes.App.Backend.Catalog.Domain.Entities;

/// <summary>
/// Per-product, per-channel input data that feeds the margin calculation.
/// Changes create a new versioned record (new ValidFrom) rather than overwriting.
/// </summary>
public class ProductChannelInput : ITenantScoped
{
    public Guid Id { get; set; }

    public Guid ProductId { get; set; }

    public Guid ChannelParameterSetId { get; set; }

    /// <summary>Cost of raw materials for one product unit (manual operator input).</summary>
    public decimal RawMaterialCost { get; set; }

    /// <summary>Logistics cost from supplier to operator's warehouse (manual operator input).</summary>
    public decimal LogisticsToCost { get; set; }

    /// <summary>Listing price before any discounts, in currency.</summary>
    public decimal PriceWithoutDiscount { get; set; }

    /// <summary>Date from which this input version is effective.</summary>
    public DateOnly ValidFrom { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Product Product { get; set; } = default!;

    public ChannelParameterSet ChannelParameterSet { get; set; } = default!;

    public ICollection<MarginCalculationSnapshot> MarginCalculationSnapshots { get; set; } = new List<MarginCalculationSnapshot>();
}
