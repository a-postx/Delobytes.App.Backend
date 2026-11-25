using Delobytes.App.Backend.Catalog.Application.Interfaces;
using Delobytes.App.Backend.Catalog.Domain;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Catalog.Application.Products;

/// <summary>
/// Barcode rules of the import. Two sets are maintained and never mixed: the channel's own copy on
/// the link, which every import replaces as a whole, and the product's canonical set, which is
/// written once when the product is created and belongs to the operator from then on.
/// </summary>
public class CardBarcodePolicy
{
    private readonly IProductImportStore _store;
    private readonly ILogger<CardBarcodePolicy> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CardBarcodePolicy"/> class.
    /// </summary>
    /// <param name="store">Persistence port of the import.</param>
    /// <param name="logger">Logger instance.</param>
    public CardBarcodePolicy(IProductImportStore store, ILogger<CardBarcodePolicy> logger)
    {
        _store = store;
        _logger = logger;
    }

    /// <summary>
    /// Gives a product its canonical barcodes at the moment it is created from a card. Values the
    /// tenant already holds as another product's canonical barcode are left out: the canonical set
    /// stays unique per tenant, and a card that reuses a value another product already owns must
    /// not fail on it. Those values remain on the channel link, which is where a repeated barcode
    /// is allowed to live.
    /// </summary>
    /// <remarks>
    /// Every other import path leaves <see cref="Product.Barcodes"/> alone, because a canonical
    /// barcode is the operator's identity for the product and a later sync must not rewrite it. The
    /// first value of a new product is marked default, so the label printed for it is stable. Every
    /// row is written as <see cref="BarcodeTypes.Ean13"/>: the field carries a symbology, while the
    /// type the channel reports stays on the link.
    /// </remarks>
    /// <param name="product">Product being created.</param>
    /// <param name="channelId">Channel the card belongs to, for logging.</param>
    /// <param name="externalProductId">Card identifier, for logging.</param>
    /// <param name="barcodes">Normalised barcodes of the card.</param>
    /// <param name="now">Time of the import.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task AssignCanonicalAsync(
        Product product,
        Guid channelId,
        string externalProductId,
        IReadOnlyList<string> barcodes,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (barcodes.Count == 0)
        {
            return;
        }

        IReadOnlySet<string> taken = await _store.FindTakenCanonicalBarcodesAsync(
            barcodes,
            product.Id,
            cancellationToken);

        bool anyAssigned = false;

        foreach (string barcodeValue in barcodes)
        {
            if (taken.Contains(barcodeValue))
            {
                _logger.LogWarning(
                    "ChannelId={ChannelId}, ExternalProductId={ExternalProductId}: barcode {Barcode} is already the canonical barcode of another product; keeping it on the channel link only.",
                    channelId,
                    externalProductId,
                    barcodeValue);

                continue;
            }

            _store.AddProductBarcode(new ProductBarcode
            {
                ProductId = product.Id,
                Value = barcodeValue,
                Type = BarcodeTypes.Ean13,
                IsDefault = !anyAssigned,
                CreatedAt = now,
            });

            anyAssigned = true;
        }
    }

    /// <summary>
    /// Replaces the barcodes the channel reports for a card with <paramref name="barcodes"/>:
    /// values the channel no longer reports are removed, the rest are added. Returns whether the
    /// stored set changed.
    /// </summary>
    /// <remarks>
    /// A full replacement, not an accumulation: the card's barcode set is owned by the channel, so
    /// a value it stopped reporting is stale here even though the product may still carry it
    /// canonically. The two sets are allowed to disagree — the canonical one is the catalogue's
    /// identity for the product, this one is a copy of what the channel currently says.
    /// </remarks>
    /// <param name="link">Link whose set is replaced.</param>
    /// <param name="barcodes">Normalised barcodes of the card.</param>
    /// <param name="barcodeType">Type the channel reports for its barcodes.</param>
    /// <param name="now">Time of the import.</param>
    /// <returns>True when a row was added or removed.</returns>
    public bool ReplaceChannelSet(
        ChannelProduct link,
        IReadOnlyList<string> barcodes,
        string barcodeType,
        DateTimeOffset now)
    {
        HashSet<string> newValues = barcodes.ToHashSet();

        // A card that reports no barcode at all is read as "the channel said nothing about them",
        // not as "the channel dropped every barcode": the two are indistinguishable in the payload,
        // and wiping the stored set on a partial response would destroy data no later import can
        // reconstruct. Removal is therefore driven only by a card that does report a set.
        if (newValues.Count == 0)
        {
            return false;
        }

        bool hasChanges = false;

        foreach (ChannelProductBarcode existing in link.Barcodes.ToList())
        {
            if (!newValues.Contains(existing.Value))
            {
                _store.RemoveChannelProductBarcode(existing);
                link.Barcodes.Remove(existing);
                hasChanges = true;
            }
        }

        HashSet<string> storedValues = link.Barcodes
            .Select(barcode => barcode.Value)
            .ToHashSet();

        foreach (string barcodeValue in newValues)
        {
            if (storedValues.Contains(barcodeValue))
            {
                continue;
            }

            ChannelProductBarcode barcode = new ChannelProductBarcode
            {
                ChannelProductId = link.Id,
                Value = barcodeValue,
                Type = barcodeType,
                CreatedAt = now,
            };

            _store.AddChannelProductBarcode(barcode);

            // EF fixes the navigation up by itself once the child is tracked; the guard only keeps
            // the tracked collection free of a second reference to the same row.
            if (!link.Barcodes.Contains(barcode))
            {
                link.Barcodes.Add(barcode);
            }

            hasChanges = true;
        }

        return hasChanges;
    }
}
