using Delobytes.App.Backend.Finance.Application.ReadModels;
using Delobytes.App.Backend.Finance.Domain.Enums;

namespace Delobytes.App.Backend.Finance.Application.ProductTable;

/// <summary>Population a product table is restricted to.</summary>
public enum PnlProductSegment
{
    All = 0,
    Loss = 1,
    WithoutCost = 2,
}

/// <summary>Metric a product table is ordered by.</summary>
public enum PnlProductSortKey
{
    Sku = 0,
    Revenue = 1,
    Deductions = 2,
    NetRevenue = 3,
    SoldCost = 4,
    Margin = 5,
    ContributionMarginPercent = 6,
    ReturnOnSoldCostPercent = 7,
    QuantitySold = 8,
    BuyoutPercent = 9,
}

/// <summary>How much of a product's net revenue carries a known production cost.</summary>
public enum PnlProductCostCoverage
{
    Complete = 0,
    Partial = 1,
    Missing = 2,
}

/// <summary>One product over the whole requested range.</summary>
public sealed class PnlProductRow : IPnlSkuLabelTarget
{
    public Guid? ProductId { get; set; }

    /// <summary>Gets or sets the catalogue SKU; null while the label is not resolved.</summary>
    public string? Sku { get; set; }

    public int Lines { get; set; }

    public decimal? RevenueAmount { get; set; }

    /// <summary>Gets or sets the net revenue minus the revenue: negative when money was withheld.</summary>
    public decimal? DeductionsAmount { get; set; }

    public decimal? NetRevenueAmount { get; set; }

    public decimal? CommissionAmount { get; set; }

    public decimal? DeliveryFeeAmount { get; set; }

    public decimal? RefundAmount { get; set; }

    public decimal? AcquiringAmount { get; set; }

    public decimal? CompensationAmount { get; set; }

    /// <summary>Gets or sets the production cost of the sold units; null while the margin is unknown.</summary>
    public decimal? SoldCostAmount { get; set; }

    public decimal NetRevenueWithoutCostAmount { get; set; }

    public decimal? Margin { get; set; }

    public decimal? ContributionMarginPercent { get; set; }

    public decimal? ReturnOnSoldCostPercent { get; set; }

    /// <summary>Gets or sets the share of the revenue the margin covers, in percent.</summary>
    public decimal? MarginCoveragePercent { get; set; }

    public PnlProductCostCoverage CostCoverage { get; set; }

    public int QuantityOrdered { get; set; }

    public int QuantitySold { get; set; }

    public int NonBuyoutQuantity { get; set; }

    public decimal? BuyoutPercent { get; set; }

    public decimal InvestedCostAmount { get; set; }

    /// <summary>
    /// Gets or sets the cost tied up in units that never sold. Known only when the invested-cost and
    /// margin populations are the same lines.
    /// </summary>
    public decimal? FrozenCostAmount { get; set; }

    public decimal? RoiPercent { get; set; }

    public bool IsRoiComplete { get; set; }

    public decimal? EstimatedTax { get; set; }

    public decimal? TaxAdjustedMargin { get; set; }

    public decimal? TaxAdjustedRoiPercent { get; set; }

    public bool IsTaxAdjustedMarginComplete { get; set; }

    public bool IsTaxAdjustedRoiComplete { get; set; }

    public decimal ProvisionalRevenueAmount { get; set; }

    public decimal OverdueRevenueAmount { get; set; }

    /// <summary>Gets or sets the share of the revenue whose figures are final, in percent.</summary>
    public decimal? FinalRevenuePercent { get; set; }

    public decimal? ControlDifferenceAmount { get; set; }

    public PnlLineCompleteness CompletenessState { get; set; }
}
