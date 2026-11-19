namespace Delobytes.App.Backend.Finance.Application.Queries;

/// <summary>One P&amp;L line returned by the read model.</summary>
public sealed class PnlLineView
{
    public Guid OrderLineId { get; set; }

    public Guid? ChannelId { get; set; }

    public Guid? ProductId { get; set; }

    /// <summary>Gets or sets the channel product Sales linked the line to, when known.</summary>
    public Guid? ChannelProductId { get; set; }

    public bool HasProductLink { get; set; }

    public DateOnly? SaleDate { get; set; }

    public string Currency { get; set; } = default!;

    public decimal? RevenueAmount { get; set; }

    public decimal? CompensationAmount { get; set; }

    public decimal? NetRevenueAmount { get; set; }

    public decimal? ProductionCostAmount { get; set; }

    public decimal? EstimatedVat { get; set; }

    public decimal? EstimatedTax { get; set; }

    public decimal? MarketplaceFee { get; set; }

    /// <summary>
    /// Gets or sets the margin before tax: net revenue minus the production cost. Null while the
    /// cost is unknown or incomplete, and never dependent on the tenant's tax settings.
    /// </summary>
    public decimal? Margin { get; set; }

    /// <summary>
    /// Gets or sets the margin net of the estimated tax. Null when the pre-tax margin or the tax
    /// profile is unknown: the tax layer is optional and does not gate the pre-tax figure.
    /// </summary>
    public decimal? TaxAdjustedMargin { get; set; }

    public decimal? MarginPercent { get; set; }

    public decimal? Roi { get; set; }

    public bool CostComplete { get; set; }

    public string? CostStatus { get; set; }

    public bool ComponentsReconciled { get; set; }

    /// <summary>Gets or sets whether every expected document of the sale has arrived.</summary>
    public Domain.Enums.PnlLineCompleteness CompletenessState { get; set; }

    /// <summary>Gets or sets the document types still expected, by name.</summary>
    public List<string> MissingDocuments { get; set; } = new List<string>();

    /// <summary>Gets or sets the earliest deadline among the open expectations, or null.</summary>
    public DateTimeOffset? ExpectedBy { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the figures are provisional because a document is
    /// still missing. A provisional line must not be reported as final.
    /// </summary>
    public bool IsProvisional { get; set; }
}
