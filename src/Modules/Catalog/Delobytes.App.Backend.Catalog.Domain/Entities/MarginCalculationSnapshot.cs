using Delobytes.App.Backend.Catalog.Domain.Enums;
using Delobytes.App.Backend.Contracts.Accounting;
using Delobytes.App.Backend.Contracts.Interfaces;

namespace Delobytes.App.Backend.Catalog.Domain.Entities;

/// <summary>
/// An immutable record of a margin calculation at a specific point in time.
/// All input parameter versions used are stored here; the snapshot is never
/// recalculated when parameters change later, preserving historical accuracy.
/// </summary>
/// <remarks>
/// The turnover tax (УСН «Доходы») is reported below the margin, never inside it: it is charged on
/// revenue, so folding it into <see cref="Margin"/> would make the margin of a product depend on
/// the owner's tax regime and stop two products being comparable. VAT stays out of both, because
/// it is collected from the buyer on top of the price and is a liability, not an expense.
/// </remarks>
public class MarginCalculationSnapshot : ITenantScoped
{
    public Guid Id { get; set; }

    public Guid ProductChannelInputId { get; set; }

    public Guid WorkRateId { get; set; }

    public Guid ProductCostSnapshotId { get; set; }

    // ── Calculated cost components ───────────────────────────────────────────

    /// <summary>
    /// Aggregate of all channel-level cost entries for the product+channel pair
    /// at the time of calculation.
    /// </summary>
    public decimal ChannelCostTotal { get; set; }

    /// <summary>Total cost of goods: sum of all cost components.</summary>
    public decimal TotalCost
    {
        get { return ProductCostSnapshot.TotalCost + ChannelCostTotal; }
    }

    // ── Calculated revenue components ────────────────────────────────────────

    /// <summary>Price the buyer paid, before any deduction.</summary>
    public decimal BuyerPrice { get; set; }

    /// <summary>Gets or sets the price the tax was calculated from.</summary>
    public decimal TaxBaseAmount { get; set; }

    /// <summary>
    /// Gets or sets which reported price <see cref="TaxBaseAmount"/> was taken from.
    /// </summary>
    public TaxBase TaxBase { get; set; } = TaxBase.BuyerPrice;

    public decimal CommissionAmount { get; set; }

    public decimal AcquiringAmount { get; set; }

    /// <summary>
    /// Gets or sets the turnover tax: <see cref="TaxBaseAmount"/> less VAT, multiplied by the rate
    /// of the tenant profile in effect at <see cref="CalculatedAt"/>.
    /// </summary>
    public decimal TaxAmount { get; set; }

    /// <summary>Gets or sets the rate applied, so the figure stays explainable after a rate change.</summary>
    public decimal TaxRatePercent { get; set; }

    /// <summary>Gets or sets the tax regime the rate belongs to.</summary>
    public TaxRegime AppliedTaxRegime { get; set; } = TaxRegime.UsnIncome;

    /// <summary>
    /// Gets or sets the VAT collected from the buyer. Never part of revenue and never an expense.
    /// </summary>
    /// <remarks>
    /// The reduced rates (5%/7%) do not permit input VAT to be deducted, so the whole amount is due
    /// to the budget. It is carried here only so the ledger can be reproduced from the snapshot.
    /// </remarks>
    public decimal VatAmount { get; set; }

    /// <summary>Gets or sets the VAT rate applied to this calculation.</summary>
    public VatType Vat { get; set; } = VatType.None;

    /// <summary>Чистая выручка (сырая - комиссия - эквайринг - логистика). Налог сюда не входит.</summary>
    public decimal NetRevenue { get; set; }

    // ── Margin ────────────────────────────────────────────────────────────────

    /// <summary>Net revenue less total cost. Charged before tax.</summary>
    public decimal Margin { get; set; }

    /// <summary>Margin as a fraction of net revenue (e.g. 0.25 = 25%).</summary>
    public decimal MarginPercent { get; set; }

    /// <summary>
    /// Gets or sets the margin after the turnover tax. Reported beside <see cref="Margin"/>, which
    /// stays the comparable, pre-tax figure.
    /// </summary>
    public decimal MarginAfterTax { get; set; }

    public DateTimeOffset CalculatedAt { get; set; }

    public ProductChannelInput ProductChannelInput { get; set; } = default!;

    public ProductCostSnapshot ProductCostSnapshot { get; set; } = default!;
}
