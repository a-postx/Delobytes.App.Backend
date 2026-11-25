using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Integrations.Contracts.Models;

namespace Delobytes.App.Backend.Catalog.Application.Products;

/// <summary>
/// Packing units of a product as the import maintains them: one canonical row that the catalogue
/// owns and one row per channel that reports its own packaging.
/// </summary>
public static class PackingUnitPolicy
{
    /// <summary>
    /// Reports whether the card carries a complete, usable dimension set. A marketplace that
    /// omits a measurement sends null, and a zero means "unknown", not "zero-sized"; both must be
    /// treated as absent, because the columns are non-nullable decimals and a written zero would
    /// be indistinguishable from a real size. The comparison is lifted, so null yields false
    /// without being dereferenced.
    /// </summary>
    /// <param name="card">Card of the channel.</param>
    /// <returns>True when every dimension is present and positive.</returns>
    public static bool HasUsableDimensions(ChannelProductCard card)
    {
        return card.LengthCm > 0 && card.WidthCm > 0 && card.HeightCm > 0;
    }

    /// <summary>
    /// Creates the product's canonical packing unit when it has none and the card reports usable
    /// dimensions. An existing canonical unit is never touched here: it is the catalogue's own
    /// value, written once, and the import only fills the gap that the creating import left.
    /// </summary>
    /// <param name="product">Product that owns the units.</param>
    /// <param name="card">Card of the channel.</param>
    /// <param name="now">Time of the import.</param>
    /// <returns>True when a row was added.</returns>
    public static bool EnsureCanonical(Product product, ChannelProductCard card, DateTimeOffset now)
    {
        if (!HasUsableDimensions(card))
        {
            return false;
        }

        // Any canonical row, active or not, occupies the filtered unique index. Adding a second
        // one would fail the insert and with it the card's savepoint.
        if (product.PackingUnits.Any(unit => unit.ChannelId == null))
        {
            return false;
        }

        product.PackingUnits.Add(new PackingUnit
        {
            ProductId = product.Id,
            ChannelId = null,
            LengthCm = card.LengthCm!.Value,
            WidthCm = card.WidthCm!.Value,
            HeightCm = card.HeightCm!.Value,
            WeightKg = card.WeightKg,
            IsActive = true,
            CreatedAt = now,
        });

        return true;
    }

    /// <summary>
    /// Creates or refreshes the packing unit that this channel reports. The canonical row is left
    /// alone: the marketplace describes its own packing, not the catalogue's.
    /// </summary>
    /// <param name="product">Product that owns the units.</param>
    /// <param name="channelId">Channel the card belongs to.</param>
    /// <param name="card">Card of the channel.</param>
    /// <param name="now">Time of the import.</param>
    /// <returns>
    /// True when a row was created or changed. A card with an incomplete dimension set returns
    /// false and leaves an existing row byte-identical — an omitted measurement must never
    /// overwrite a value the channel reported earlier with a zero.
    /// </returns>
    public static bool SynchronizeChannel(
        Product product,
        Guid channelId,
        ChannelProductCard card,
        DateTimeOffset now)
    {
        if (!HasUsableDimensions(card))
        {
            return false;
        }

        // Matched by channel regardless of IsActive: the unique index on (ProductId, ChannelId)
        // does not filter inactivity, so a new row would collide with a deactivated one.
        PackingUnit? existing = product.PackingUnits.FirstOrDefault(unit => unit.ChannelId == channelId);

        if (existing == null)
        {
            product.PackingUnits.Add(new PackingUnit
            {
                ProductId = product.Id,
                ChannelId = channelId,
                LengthCm = card.LengthCm!.Value,
                WidthCm = card.WidthCm!.Value,
                HeightCm = card.HeightCm!.Value,
                WeightKg = card.WeightKg,
                IsActive = true,
                CreatedAt = now,
            });

            return true;
        }

        bool hasChanges = false;

        if (existing.LengthCm != card.LengthCm!.Value)
        {
            existing.LengthCm = card.LengthCm.Value;
            hasChanges = true;
        }

        if (existing.WidthCm != card.WidthCm!.Value)
        {
            existing.WidthCm = card.WidthCm.Value;
            hasChanges = true;
        }

        if (existing.HeightCm != card.HeightCm!.Value)
        {
            existing.HeightCm = card.HeightCm.Value;
            hasChanges = true;
        }

        if (existing.WeightKg != card.WeightKg)
        {
            existing.WeightKg = card.WeightKg;
            hasChanges = true;
        }

        if (!existing.IsActive)
        {
            existing.IsActive = true;
            hasChanges = true;
        }

        if (hasChanges)
        {
            existing.UpdatedAt = now;
        }

        return hasChanges;
    }
}
