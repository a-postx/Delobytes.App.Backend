using Delobytes.App.Backend.Finance.Domain.Enums;

namespace Delobytes.App.Backend.Finance.Application.ReadModels;

/// <summary>
/// One P&amp;L line as the read model sees it.
/// </summary>
public sealed class PnlLineRow
{
    /// <summary>Gets or sets the P&amp;L line identifier; the keyset cursor is built from it.</summary>
    public Guid Id { get; set; }

    public Guid OrderLineId { get; set; }

    public Guid? ChannelId { get; set; }

    /// <summary>
    /// Gets or sets the catalogue product of the line, or null while no cost fact exists or the
    /// channel product is not linked to a product.
    /// </summary>
    public Guid? ProductId { get; set; }

    /// <summary>Gets or sets the channel product Sales linked the line to, when known.</summary>
    public Guid? ChannelProductId { get; set; }

    public string Currency { get; set; } = default!;

    public DateOnly? SaleDate { get; set; }

    public PnlLineState State { get; set; }

    public OrderLineCostStatus? CostStatus { get; set; }

    public decimal? RevenueAmount { get; set; }

    public decimal? CommissionAmount { get; set; }

    public decimal? AcquiringAmount { get; set; }

    public decimal? DeliveryFeeAmount { get; set; }

    public decimal? RefundAmount { get; set; }

    public decimal? CompensationAmount { get; set; }

    public decimal? NetRevenueAmount { get; set; }

    public decimal? ControlTotalAmount { get; set; }

    public decimal? ControlDifferenceAmount { get; set; }

    public decimal? ProductionCostAmount { get; set; }

    public bool? ProductionCostIsComplete { get; set; }

    /// <summary>Gets or sets whether every document the sale is expected to produce has arrived.</summary>
    public PnlLineCompleteness CompletenessState { get; set; }

    /// <summary>Gets or sets the entry types the line is still waiting for, by name.</summary>
    public List<string> MissingDocumentTypes { get; set; } = new List<string>();

    /// <summary>Gets or sets the earliest deadline among the open expectations, or null.</summary>
    public DateTimeOffset? ExpectedBy { get; set; }
}
