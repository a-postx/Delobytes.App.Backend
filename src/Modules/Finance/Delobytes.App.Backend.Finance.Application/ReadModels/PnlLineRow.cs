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

    /// <summary>
    /// Gets or sets the number of units of the line the journal reverses. The sold quantity is what
    /// the returned units leave of <see cref="Quantity"/>.
    /// </summary>
    public int ReturnedQuantity { get; set; }

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

    /// <summary>
    /// Gets or sets the margin before tax as the database computes it, so a page can be ordered by
    /// it. It repeats <see cref="PnlTaxCalculator.LineMargin"/>, which runs only after
    /// materialisation; the two are pinned to each other by the read store tests.
    /// </summary>
    public decimal? MarginAmount { get; set; }

    /// <summary>
    /// Gets or sets the resolved cost of one unit, or null when the cost fact carries only a line
    /// total. It describes the ordered units: the cost of the whole line lets the caller value the
    /// units that never sold as well.
    /// </summary>
    public decimal? UnitTotalCost { get; set; }

    /// <summary>
    /// Gets or sets the resolved total cost of the line the fact was calculated for, or null.
    /// </summary>
    public decimal? TotalCost { get; set; }

    /// <summary>
    /// Gets or sets the quantity the total cost was resolved for, or null. It can differ from the
    /// ordered quantity, in which case the total has to be prorated.
    /// </summary>
    public int? CostQuantity { get; set; }

    /// <summary>
    /// Gets or sets whether the cost fact covers every input of the line, or null when no cost fact
    /// exists. Unlike <see cref="ProductionCostIsComplete"/>, it is set for a line that never sold:
    /// its units are still an investment even though no cost of goods is charged.
    /// </summary>
    public bool? CostIsComplete { get; set; }

    /// <summary>Gets or sets whether every document the sale is expected to produce has arrived.</summary>
    public PnlLineCompleteness CompletenessState { get; set; }

    /// <summary>Gets or sets the entry types the line is still waiting for, by name.</summary>
    public List<string> MissingDocumentTypes { get; set; } = new List<string>();

    /// <summary>Gets or sets the earliest deadline among the open expectations, or null.</summary>
    public DateTimeOffset? ExpectedBy { get; set; }
}
