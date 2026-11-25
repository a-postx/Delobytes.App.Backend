using Delobytes.App.Backend.Catalog.Application.Interfaces;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Integrations.Contracts.Models;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Catalog.Application.Products;

/// <summary>
/// Finds the product an unknown card belongs to: by the channel's own barcodes first, then by the
/// tenant's canonical barcodes, then by the seller article the card carries
/// (<c>vendorCode</c> / <c>offer_id</c>). <see cref="Product.Sku"/> is deliberately never
/// consulted: it is user-editable, so using it as a key would re-route an import after a rename.
/// </summary>
/// <remarks>
/// The ladders exist because the three keys carry different guarantees. A channel barcode is
/// authoritative for the importing channel alone: another channel may report the same value for
/// a different product, so a match there is only trusted inside this channel. The canonical
/// barcode is unique per tenant, which is what lets a card from a newly connected channel find
/// the product an older channel already created. The article is the last resort and is the only
/// key a card can carry without any barcode at all.
/// Coming back with several candidates is reported rather than resolved: reading it as either
/// "one of them" or "none of them" would attach the card to a product the operator did not
/// choose, or create a duplicate of one that already exists.
/// </remarks>
public class ProductCardMatcher
{
    private readonly IProductImportStore _store;
    private readonly ILogger<ProductCardMatcher> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProductCardMatcher"/> class.
    /// </summary>
    /// <param name="store">Persistence port of the import.</param>
    /// <param name="logger">Logger instance.</param>
    public ProductCardMatcher(IProductImportStore store, ILogger<ProductCardMatcher> logger)
    {
        _store = store;
        _logger = logger;
    }

    /// <summary>Resolves the card against the catalogue.</summary>
    /// <param name="card">Card of the channel, as the channel reports it.</param>
    /// <param name="barcodes">Normalised barcodes of the card.</param>
    /// <param name="channelId">Channel the card belongs to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One product, no product, or nothing the import can choose between.</returns>
    public async Task<ProductMatch> MatchAsync(
        ChannelProductCard card,
        IReadOnlyList<string> barcodes,
        Guid channelId,
        CancellationToken cancellationToken)
    {
        bool barcodeWasAmbiguous = false;

        if (barcodes.Count > 0)
        {
            IReadOnlyList<Product> productsByChannelBarcode = await _store
                .FindProductsByChannelBarcodesAsync(channelId, barcodes, cancellationToken);

            if (productsByChannelBarcode.Count == 1)
            {
                return new ProductMatch(productsByChannelBarcode[0], IsAmbiguous: false);
            }

            if (productsByChannelBarcode.Count > 1)
            {
                barcodeWasAmbiguous = true;

                _logger.LogWarning(
                    "ChannelId={ChannelId}, ExternalProductId={ExternalProductId}: {Count} products of this channel report one of the card's barcodes. Falling back to the canonical barcodes and then to the seller article.",
                    channelId,
                    card.ExternalProductId,
                    productsByChannelBarcode.Count);
            }

            IReadOnlyList<Product> productsByCanonicalBarcode = await _store
                .FindProductsByBarcodesAsync(barcodes, cancellationToken);

            if (productsByCanonicalBarcode.Count == 1)
            {
                return new ProductMatch(productsByCanonicalBarcode[0], IsAmbiguous: false);
            }

            if (productsByCanonicalBarcode.Count > 1)
            {
                barcodeWasAmbiguous = true;

                _logger.LogWarning(
                    "ChannelId={ChannelId}, ExternalProductId={ExternalProductId}: {Count} products share a canonical barcode with the card. Falling back to the seller article.",
                    channelId,
                    card.ExternalProductId,
                    productsByCanonicalBarcode.Count);
            }
        }

        string offerId = ProductCardNormalizer.TrimOfferId(card.OfferId);

        if (offerId.Length > 0)
        {
            IReadOnlyList<Product> productsByExternalSku = await _store
                .FindProductsByExternalSkuAsync(offerId, cancellationToken);

            if (productsByExternalSku.Count == 1)
            {
                return new ProductMatch(productsByExternalSku[0], IsAmbiguous: false);
            }

            if (productsByExternalSku.Count > 1)
            {
                _logger.LogInformation(
                    "ChannelId={ChannelId}, ExternalProductId={ExternalProductId}: seller article {OfferId} matches {Count} products. Skipping the card instead of guessing.",
                    channelId,
                    card.ExternalProductId,
                    offerId,
                    productsByExternalSku.Count);

                return new ProductMatch(null, IsAmbiguous: true);
            }
        }

        // Several barcodes pointing at different products is a contradiction in the card, not an
        // absence of candidates: creating a product here would duplicate one of the two the card
        // already points at.
        return new ProductMatch(null, barcodeWasAmbiguous);
    }
}

/// <summary>
/// Result of matching a card to a catalogue product: either exactly one candidate, or none,
/// or nothing that can be chosen between. The third state is why this is not a plain
/// <see cref="Product"/> reference — an ambiguous match must not be read as "no candidate".
/// </summary>
/// <param name="Product">The single matching product, or <c>null</c>.</param>
/// <param name="IsAmbiguous">Whether the card's identifiers point at several products.</param>
public sealed record ProductMatch(Product? Product, bool IsAmbiguous);
