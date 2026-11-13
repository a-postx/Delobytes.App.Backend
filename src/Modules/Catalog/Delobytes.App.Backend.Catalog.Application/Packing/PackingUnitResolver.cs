using Delobytes.App.Backend.Catalog.Domain.Entities;

namespace Delobytes.App.Backend.Catalog.Application.Packing;

/// <summary>
/// Selects the packing unit of a product for a given purpose.
/// </summary>
/// <remarks>
/// A product carries at most one canonical row (<c>ChannelId == null</c>) and at most one row per
/// channel, and neither is mandatory: products predate channel-specific units, and a marketplace
/// card may report no dimensions at all. Every method therefore returns <c>null</c> instead of
/// throwing, and callers that compute volumes or tariffs must handle that instead of dereferencing.
/// </remarks>
public static class PackingUnitResolver
{
    /// <summary>
    /// Returns the product's canonical unit — the one the catalogue itself works with — or
    /// <c>null</c> when the product has none. Channel-reported rows are never returned.
    /// </summary>
    public static PackingUnit? ResolveCanonical(Product product)
    {
        return product.PackingUnits.FirstOrDefault(unit => unit.IsActive && unit.ChannelId == null);
    }

    /// <summary>
    /// Returns the unit reported by <paramref name="channelId"/>, or <c>null</c> when that channel
    /// reported none. The canonical row is never returned.
    /// </summary>
    public static PackingUnit? ResolveForChannel(Product product, Guid channelId)
    {
        return product.PackingUnits.FirstOrDefault(unit => unit.IsActive && unit.ChannelId == channelId);
    }
}
