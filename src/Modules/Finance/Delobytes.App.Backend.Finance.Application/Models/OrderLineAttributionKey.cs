namespace Delobytes.App.Backend.Finance.Application.Models;

/// <summary>
/// The attribution key a journal entry is matched to Sales order lines by. Either the unit identity
/// is set (Wildberries <c>srid</c>, Ozon posting number) or the order and the product are, never
/// both: an Ozon <c>ITEM</c> accrual carries no posting, and a posting-level entry is matched
/// without consulting the order.
/// </summary>
public readonly record struct OrderLineAttributionKey(
    Guid ChannelId,
    string? ExternalLineId,
    string? ExternalOrderId,
    string? ExternalProductId)
{
    /// <summary>
    /// Builds the key of an entry that carries a unit identity.
    /// </summary>
    /// <param name="channelId">Channel identifier.</param>
    /// <param name="externalLineId">Channel-side unit identity.</param>
    /// <returns>The attribution key.</returns>
    public static OrderLineAttributionKey ForLine(Guid channelId, string externalLineId)
    {
        return new OrderLineAttributionKey(channelId, externalLineId, null, null);
    }

    /// <summary>
    /// Builds the key of an entry that carries an order and a product instead of a unit identity.
    /// </summary>
    /// <param name="channelId">Channel identifier.</param>
    /// <param name="externalOrderId">Channel-side order identity.</param>
    /// <param name="externalProductId">Channel-side product identity.</param>
    /// <returns>The attribution key.</returns>
    public static OrderLineAttributionKey ForOrder(Guid channelId, string externalOrderId, string externalProductId)
    {
        return new OrderLineAttributionKey(channelId, null, externalOrderId, externalProductId);
    }
}
