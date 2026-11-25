using Delobytes.App.Backend.Catalog.Application.Interfaces;
using Delobytes.App.Backend.Catalog.Domain;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using Delobytes.App.Backend.Integrations.Contracts.Models;

namespace Delobytes.App.Backend.Catalog.Application.Products;

/// <summary>
/// Turns the channel's card into the values the catalogue stores. Everything here is a pure
/// projection: the card itself is never mutated, so a redelivered message carries the same values.
/// </summary>
public static class ProductCardNormalizer
{
    /// <summary>
    /// Mirrors ProductConfiguration: Product.Description is varchar(20000). WB allows descriptions
    /// up to 5000 characters, so a longer card would otherwise be rejected by PostgreSQL at
    /// SaveChanges and roll back the whole card.
    /// </summary>
    public const int DescriptionMaxLength = 20000;

    /// <summary>Mirrors ProductConfiguration: Product.Name is varchar(2000).</summary>
    public const int NameMaxLength = 2000;

    /// <summary>
    /// Trimmed, non-empty, distinct barcode values in the order the card reports them. The matchers
    /// compare these against trimmed columns, and a stray space would otherwise make a card look
    /// like a new product with a new barcode.
    /// </summary>
    /// <param name="barcodes">Barcodes as the channel reports them.</param>
    /// <returns>The normalised values.</returns>
    public static IReadOnlyList<string> NormalizeBarcodes(IEnumerable<string>? barcodes)
    {
        if (barcodes is null)
        {
            return Array.Empty<string>();
        }

        return barcodes
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct()
            .ToList();
    }

    /// <summary>Trims the name to the column width.</summary>
    /// <param name="name">Name as the channel reports it.</param>
    /// <returns>The value the column can hold.</returns>
    public static string TrimName(string name)
    {
        if (string.IsNullOrEmpty(name) || name.Length <= NameMaxLength)
        {
            return name;
        }

        return name[..NameMaxLength];
    }

    /// <summary>Trims the description to the column width.</summary>
    /// <param name="description">Description as the channel reports it.</param>
    /// <returns>The value the column can hold.</returns>
    public static string? TrimDescription(string? description)
    {
        if (string.IsNullOrEmpty(description) || description.Length <= DescriptionMaxLength)
        {
            return description;
        }

        return description[..DescriptionMaxLength];
    }

    /// <summary>Returns the seller article as the catalogue stores it: trimmed, empty when absent.</summary>
    /// <param name="offerId">Article as the channel reports it.</param>
    /// <returns>The trimmed article or an empty string.</returns>
    public static string TrimOfferId(string? offerId)
    {
        return (offerId ?? string.Empty).Trim();
    }

    /// <summary>
    /// Projects the card's photo variants onto the importer's sources. Display order and variant
    /// names come from the channel mapper, so this stays independent of one marketplace's sizes.
    /// </summary>
    /// <param name="card">Card of the channel.</param>
    /// <returns>The sources to download, in the order the card declares them.</returns>
    public static List<MarketplacePhotoSource> BuildPhotoSources(ChannelProductCard card)
    {
        List<MarketplacePhotoSource> sources = new List<MarketplacePhotoSource>(card.Photos.Count);

        foreach (ChannelProductPhoto photo in card.Photos)
        {
            if (string.IsNullOrEmpty(photo.Url))
            {
                continue;
            }

            sources.Add(new MarketplacePhotoSource
            {
                Url = photo.Url,
                ExternalId = $"{card.ExternalProductId}_{photo.DisplayOrder}_{photo.SizeVariant}",
                DisplayOrder = photo.DisplayOrder,
                SizeVariant = photo.SizeVariant,
                Width = photo.Width,
                Height = photo.Height,
                Source = ResolvePhotoSource(card),
            });
        }

        return sources;
    }

    /// <summary>
    /// Type the channel reports for its barcode. Used for
    /// <see cref="Delobytes.App.Backend.Catalog.Domain.Entities.ChannelProductBarcode"/> only:
    /// canonical barcodes carry a symbology (<see cref="BarcodeTypes.Ean13"/>), not a channel code.
    /// </summary>
    /// <param name="card">Card of the channel.</param>
    /// <returns>The barcode type of the card.</returns>
    public static string ResolveBarcodeType(ChannelProductCard card)
    {
        return card.BarcodeType ?? card.ChannelCode;
    }

    /// <summary>Maps the channel code to the value stored as <c>ProductPhoto.Source</c>.</summary>
    /// <param name="card">Card of the channel.</param>
    /// <returns>Known source name of the channel.</returns>
    public static string ResolvePhotoSource(ChannelProductCard card)
    {
        return card.ChannelCode == ChannelCodes.Wildberries
            ? MarketplacePhotoSources.Wildberries
            : card.ChannelCode;
    }

    /// <summary>Maps the channel code to the product's creation source.</summary>
    /// <param name="card">Card of the channel.</param>
    /// <returns>Creation source to record on a product this card creates.</returns>
    public static CreationSource ResolveCreationSource(ChannelProductCard card)
    {
        return card.ChannelCode == ChannelCodes.Wildberries
            ? CreationSource.WildberriesImport
            : CreationSource.Manual;
    }
}
