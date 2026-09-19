using Delobytes.App.Backend.Contracts.Interfaces;

namespace Delobytes.App.Backend.Catalog.Domain.Entities;

/// <summary>
/// An immutable record of a margin calculation at a specific point in time.
/// All input parameter versions used are stored here; the snapshot is never
/// recalculated when parameters change later, preserving historical accuracy.
/// </summary>
public class MarginCalculationSnapshot : ITenantScoped
{
    public Guid Id { get; set; }

    public Guid ProductChannelInputId { get; set; }

    public Guid WorkRateId { get; set; }

    // ── Calculated cost components ───────────────────────────────────────────

    public decimal RawMaterialCost { get; set; }

    public decimal MaterialLogisticsCost { get; set; }

    public decimal WorkCost { get; set; }

    /// <summary>
    /// Aggregate of all ProductChannelCost entries for the product+channel pair
    /// at the time of calculation.
    /// </summary>
    public decimal ChannelCostTotal { get; set; }

    /// <summary>Total cost of goods: sum of all cost components.</summary>
    public decimal TotalCost { get; set; }

    // ── Calculated revenue components ────────────────────────────────────────

    public decimal BuyerPrice { get; set; }

    public decimal CommissionAmount { get; set; }

    public decimal AcquiringAmount { get; set; }

    public decimal TaxAmount { get; set; }

    /// <summary>Revenue net of commission, acquiring, and tax.</summary>
    public decimal NetRevenue { get; set; }

    // ── Margin ────────────────────────────────────────────────────────────────

    public decimal Margin { get; set; }

    /// <summary>Margin as a fraction of net revenue (e.g. 0.25 = 25%).</summary>
    public decimal MarginPercent { get; set; }

    public DateTimeOffset CalculatedAt { get; set; }

    public ProductChannelInput ProductChannelInput { get; set; } = default!;
}
