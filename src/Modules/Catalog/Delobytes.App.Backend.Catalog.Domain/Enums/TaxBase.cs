namespace Delobytes.App.Backend.Catalog.Domain.Enums;

/// <summary>
/// Which of the prices the channel reports is treated as the tax base.
/// </summary>
/// <remarks>
/// Channels disagree on this and the choice changes the result, so it is a stored parameter of the
/// calculation rather than a constant: Wildberries reports the buyer-facing
/// <c>finishedPrice</c> already reduced by its own promotion, while an Ozon discount is funded by
/// the platform and does not reduce seller revenue.
/// </remarks>
public enum TaxBase
{
    /// <summary>
    /// The price the buyer paid, after every discount including the platform's own
    /// (Wildberries <c>finishedPrice</c>, Ozon <c>customer_price</c>).
    /// </summary>
    BuyerPrice = 1,

    /// <summary>
    /// The seller's price after the seller's own discount but before the platform's promotion
    /// (Wildberries <c>priceWithDisc</c>, Ozon <c>price</c>).
    /// </summary>
    SellerPrice = 2,

    /// <summary>
    /// The price before any discount (Wildberries <c>totalPrice</c>, Ozon <c>old_price</c>).
    /// </summary>
    PriceBeforeDiscount = 3,
}
