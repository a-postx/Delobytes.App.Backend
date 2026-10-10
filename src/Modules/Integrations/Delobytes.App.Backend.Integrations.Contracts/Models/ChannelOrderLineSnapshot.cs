namespace Delobytes.App.Backend.Integrations.Contracts.Models;

/// <summary>
/// Normalized snapshot of a single order line. Contract model without dependencies on EF Core
/// or HTTP client types.
/// </summary>
public record ChannelOrderLineSnapshot
{
    /// <summary>
    /// Gets the deterministic line identity derived by the channel mapper. Stable across
    /// refetches of the same order, which is what makes line upsert possible.
    /// </summary>
    public string LineKey { get; init; } = default!;

    /// <summary>
    /// Gets the channel's own line identity, where one exists.
    /// </summary>
    public string? ExternalLineId { get; init; }

    /// <summary>
    /// Gets the channel's product identifier: WB nmId, Ozon sku, Yandex Market shopSku.
    /// </summary>
    public string ExternalProductId { get; init; } = default!;

    /// <summary>
    /// Gets the seller's own SKU as reported by the channel.
    /// </summary>
    public string? OfferId { get; init; }

    /// <summary>
    /// Gets the channel's size/variant identifier, such as the WB chrtId.
    /// </summary>
    public string? ExternalSizeId { get; init; }

    /// <summary>
    /// Gets the product name as reported at order time.
    /// </summary>
    public string? ProductName { get; init; }

    /// <summary>
    /// Gets the VAT value as reported by the channel.
    /// </summary>
    public string? Vat { get; init; }

    /// <summary>
    /// Gets the quantity of units in this line.
    /// </summary>
    public int Quantity { get; init; } = 1;

    /// <summary>
    /// Gets the per-unit price the channel attributes to the sale.
    /// </summary>
    public decimal? UnitPrice { get; init; }

    /// <summary>
    /// Gets the per-unit price the buyer actually paid, where the channel reports it separately.
    /// </summary>
    public decimal? BuyerUnitPrice { get; init; }

    /// <summary>
    /// Gets the per-unit price before discount, where the channel reports one.
    /// </summary>
    public decimal? UnitPriceBeforeDiscount { get; init; }

    /// <summary>
    /// Gets the line currency.
    /// </summary>
    public string Currency { get; init; } = default!;

    /// <summary>
    /// Gets the commission the order API itself reported. Set only where the payload carries a
    /// settlement fact (Ozon financial_data), null everywhere else. Never computed here.
    /// </summary>
    public decimal? ChannelReportedCommission { get; init; }

    /// <summary>
    /// Gets the payout the order API itself reported. Set only where the payload carries a
    /// settlement fact, null everywhere else. Never computed here.
    /// </summary>
    public decimal? ChannelReportedPayout { get; init; }
}
