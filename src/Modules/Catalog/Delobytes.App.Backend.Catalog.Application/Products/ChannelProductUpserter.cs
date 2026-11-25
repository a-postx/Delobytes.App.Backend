using Delobytes.App.Backend.Catalog.Application.Interfaces;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Integrations.Contracts.Models;

namespace Delobytes.App.Backend.Catalog.Application.Products;

/// <summary>
/// The single place that writes a card onto a channel-product link: create/link use it to build the
/// link, the update path uses it to refresh one. Keeping the three paths on one routine is what
/// makes "what an import writes to a link" one rule instead of three that drift apart.
/// </summary>
/// <remarks>
/// Only channel-owned data is written here. A product's canonical fields, canonical barcodes and
/// canonical gallery belong to the operator once the product exists; the one product-level write
/// the update path performs is the missing canonical packing unit, which no later import could add
/// otherwise.
/// </remarks>
public class ChannelProductUpserter
{
    private readonly IProductImportStore _store;
    private readonly CardBarcodePolicy _barcodes;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChannelProductUpserter"/> class.
    /// </summary>
    /// <param name="store">Persistence port of the import.</param>
    /// <param name="barcodes">Barcode rules shared by every path.</param>
    public ChannelProductUpserter(IProductImportStore store, CardBarcodePolicy barcodes)
    {
        _store = store;
        _barcodes = barcodes;
    }

    /// <summary>
    /// Stages a new link between the product and the channel and fills it with the card's data.
    /// </summary>
    /// <param name="product">Tracked product the card belongs to.</param>
    /// <param name="card">Card of the channel.</param>
    /// <param name="channelId">Channel the card belongs to.</param>
    /// <param name="barcodes">Normalised barcodes of the card.</param>
    /// <param name="now">Time of the import.</param>
    /// <returns>The staged link.</returns>
    public ChannelProduct Create(
        Product product,
        ChannelProductCard card,
        Guid channelId,
        IReadOnlyList<string> barcodes,
        DateTimeOffset now)
    {
        ChannelProduct link = new ChannelProduct
        {
            ProductId = product.Id,

            // Assigned explicitly: the import reads the product of the link later, and relying on
            // the change tracker to fix the navigation up would make that read depend on how the
            // entity was staged.
            Product = product,
            ChannelId = channelId,
            ExternalProductId = card.ExternalProductId,
            ExternalSku = ProductCardNormalizer.TrimOfferId(card.OfferId),
            Name = ProductCardNormalizer.TrimName(card.Name),
            Description = ProductCardNormalizer.TrimDescription(card.Description),
            ChannelSpecificData = card.ChannelSpecificData,
            IsActive = true,
            LastSyncedAt = now,
            CreatedAt = now,
        };

        _store.AddChannelProduct(link);
        ApplyCore(link, card, barcodes, now);

        return link;
    }

    /// <summary>
    /// Refreshes an existing link with the card's data and reports whether anything changed.
    /// </summary>
    /// <param name="link">Tracked link of the card.</param>
    /// <param name="card">Card of the channel.</param>
    /// <param name="barcodes">Normalised barcodes of the card.</param>
    /// <param name="now">Time of the import.</param>
    /// <returns>True when a stored value changed.</returns>
    public bool Apply(
        ChannelProduct link,
        ChannelProductCard card,
        IReadOnlyList<string> barcodes,
        DateTimeOffset now)
    {
        bool hasChanges = ApplyCore(link, card, barcodes, now);

        if (hasChanges)
        {
            link.UpdatedAt = now;
        }

        return hasChanges;
    }

    private bool ApplyCore(
        ChannelProduct link,
        ChannelProductCard card,
        IReadOnlyList<string> barcodes,
        DateTimeOffset now)
    {
        bool hasChanges = false;

        string cardName = ProductCardNormalizer.TrimName(card.Name);
        string? cardDescription = ProductCardNormalizer.TrimDescription(card.Description);
        string cardExternalSku = ProductCardNormalizer.TrimOfferId(card.OfferId);

        if (link.Name != cardName)
        {
            link.Name = cardName;
            hasChanges = true;
        }

        if (link.Description != cardDescription)
        {
            link.Description = cardDescription;
            hasChanges = true;
        }

        if (link.ExternalSku != cardExternalSku)
        {
            link.ExternalSku = cardExternalSku;
            hasChanges = true;
        }

        if (link.ChannelSpecificData != card.ChannelSpecificData)
        {
            link.ChannelSpecificData = card.ChannelSpecificData;
            hasChanges = true;
        }

        // Reactivation is unconditional: the card is in the listing, so the link is live again,
        // regardless of what the previous run concluded.
        if (!link.IsActive)
        {
            link.IsActive = true;
            hasChanges = true;
        }

        if (_barcodes.ReplaceChannelSet(
            link,
            barcodes,
            ProductCardNormalizer.ResolveBarcodeType(card),
            now))
        {
            hasChanges = true;
        }

        if (PackingUnitPolicy.SynchronizeChannel(link.Product, link.ChannelId, card, now))
        {
            hasChanges = true;
        }

        // A product created from a card without dimensions would otherwise never gain a canonical
        // unit, because product-level fields are not rewritten by later imports. This is the one
        // product-level write the update path performs, and it happens at most once.
        if (PackingUnitPolicy.EnsureCanonical(link.Product, card, now))
        {
            hasChanges = true;
        }

        link.LastSyncedAt = now;

        return hasChanges;
    }
}
