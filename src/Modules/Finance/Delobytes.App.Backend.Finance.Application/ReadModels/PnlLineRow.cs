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

    /// <summary>
    /// Gets or sets the date of the earliest sale fact, or null when the line never sold.
    /// </summary>
    public DateOnly? SaleDate { get; set; }

    /// <summary>
    /// Gets or sets the date the line is reported under: the sale date when it sold, otherwise the
    /// first day of its financial period. A non-buyout has no sale date but still carries a delivery
    /// charge on a known day, so the period filters and the summary must not drop it.
    /// </summary>
    public DateOnly? PeriodDate { get; set; }

    public PnlLineState State { get; set; }

    /// <summary>
    /// Gets or sets the quantity of units the line carries, as Sales reported it for the order line.
    /// It is the ordered quantity: a line that never sold still carries the units it was ordered
    /// with, so the sold quantity is derived from <see cref="SaleDate"/>, not from this field.
    /// </summary>
    public int Quantity { get; set; }

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
