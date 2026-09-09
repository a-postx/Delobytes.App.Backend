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

    /// <summary>TariffGrid used for logistics cost; null when logistics cost was entered manually.</summary>
    public Guid? TariffGridId { get; set; }

    public Guid WorkRateId { get; set; }

    // ── Calculated cost components ──────────────────────────────────────────

    public decimal RawMaterialCost { get; set; }

    public decimal PackagingCost { get; set; }

    public decimal PackagingWorkCost { get; set; }

    public decimal LogisticsToMarketplaceCost { get; set; }

    /// <summary>Total cost of goods: sum of all cost components.</summary>
    public decimal TotalCost { get; set; }

    // ── Calculated revenue components ───────────────────────────────────────

    public decimal BuyerPrice { get; set; }

    public decimal CommissionAmount { get; set; }

    public decimal AcquiringAmount { get; set; }

    public decimal TaxAmount { get; set; }

    /// <summary>Revenue net of commission, acquiring, and tax.</summary>
    public decimal NetRevenue { get; set; }

    // ── Margin ──────────────────────────────────────────────────────────────

    public decimal Margin { get; set; }

    /// <summary>Margin as a fraction of net revenue (e.g. 0.25 = 25%).</summary>
    public decimal MarginPercent { get; set; }

    public DateTimeOffset CalculatedAt { get; set; }

    public ProductChannelInput ProductChannelInput { get; set; } = default!;
}
