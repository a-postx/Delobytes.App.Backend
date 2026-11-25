using System.Collections.Generic;
using System.Globalization;
using System.Text.Json.Nodes;
using Delobytes.App.Backend.Integrations.Application.SalesChannels;
using Delobytes.App.Backend.Integrations.Contracts.Models;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Ozon.Dtos;

namespace Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Ozon;

/// <summary>
/// Projects Ozon attribute payloads onto the channel-neutral card. Pure: no HTTP, no logging, no
/// binding state.
/// </summary>
internal static class OzonCardMapper
{
    // Fixed attribute id; to be replaced by category-driven resolution later.
    private const int DescriptionAttributeId = 4191;

    /// <summary>
    /// Projects one attributes item onto the channel-neutral card.
    /// </summary>
    /// <remarks>
    /// The top-level <c>depth</c>/<c>width</c>/<c>height</c>/<c>weight</c> describe the packed unit.
    /// Attributes 8415, 6433, 6469, 4082, 4497 and 8962 carry the product's own measurements and
    /// disagree with them, so they are deliberately not read here.
    /// </remarks>
    public static ChannelProductCard Map(OzonProductAttributesItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        decimal dimensionDivisor = string.Equals(item.DimensionUnit, "mm", StringComparison.OrdinalIgnoreCase)
            ? 10m
            : 1m;

        decimal weightDivisor = string.Equals(item.WeightUnit, "g", StringComparison.OrdinalIgnoreCase)
            ? 1000m
            : 1m;

        List<ChannelProductPhoto> photos = MapPhotos(item);

        return new ChannelProductCard
        {
            ExternalProductId = item.Sku.ToString(CultureInfo.InvariantCulture),
            OfferId = item.OfferId,
            Name = item.Name ?? string.Empty,
            Description = FindAttributeValue(item, DescriptionAttributeId),
            Barcodes = MapBarcodes(item),
            LengthCm = item.Depth / dimensionDivisor,
            WidthCm = item.Width / dimensionDivisor,
            HeightCm = item.Height / dimensionDivisor,
            WeightKg = item.Weight / weightDivisor,
            Photos = photos,
            ChannelSpecificData = BuildChannelSpecificData(item, photos),
            ChannelCode = ChannelCatalog.OzonCode,
            BarcodeType = ChannelCatalog.OzonCode,
        };
    }

    /// <summary>
    /// Builds the photo list the importer expects. Ozon publishes one URL per image, so each of them
    /// is rendered by the CDN into the two size variants the photo pipeline downloads, both under the
    /// same display order. The rendered bytes are WebP: the bare URL returns the original JPEG, which
    /// the pipeline rejects.
    /// </summary>
    public static List<ChannelProductPhoto> MapPhotos(OzonProductAttributesItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        List<string> urls = new List<string>();
        HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);

        if (!string.IsNullOrWhiteSpace(item.PrimaryImage) && seen.Add(item.PrimaryImage))
        {
            urls.Add(item.PrimaryImage);
        }

        foreach (string image in item.Images)
        {
            // The primary image is frequently absent from "images", but a payload that repeats it
            // must not yield a duplicate gallery entry.
            if (!string.IsNullOrWhiteSpace(image) && seen.Add(image))
            {
                urls.Add(image);
            }
        }

        List<ChannelProductPhoto> photos = new List<ChannelProductPhoto>(urls.Count * 2);

        for (int index = 0; index < urls.Count; index++)
        {
            int displayOrder = index + 1;

            photos.Add(new ChannelProductPhoto
            {
                Url = OzonPhotoUrl.ToWebP(urls[index], OzonPhotoUrl.LargeMaxSide),
                SizeVariant = "large",
                DisplayOrder = displayOrder,
            });

            photos.Add(new ChannelProductPhoto
            {
                Url = OzonPhotoUrl.ToWebP(urls[index], OzonPhotoUrl.ThumbnailMaxSide),
                SizeVariant = "thumbnail",
                DisplayOrder = displayOrder,
            });
        }

        return photos;
    }

    /// <summary>
    /// Drives the change detection of the import for an Ozon card, exactly as the Wildberries mapper
    /// does for its own. Only values that move when the seller edits the card are included; Ozon's own
    /// timestamps stay out, so a marketplace-side touch cannot report every card of every run as
    /// updated. The attributes are ordered by id, because their order in the response is not part of
    /// the card.
    /// </summary>
    private static string BuildChannelSpecificData(
        OzonProductAttributesItem item,
        IReadOnlyList<ChannelProductPhoto> photos)
    {
        return ChannelCardSnapshot.Build(
            new List<KeyValuePair<string, JsonNode?>>
            {
                new KeyValuePair<string, JsonNode?>("descriptionAttributeId", JsonValue.Create(DescriptionAttributeId)),
                new KeyValuePair<string, JsonNode?>(
                    "attributes",
                    ChannelCardSnapshot.ToOrderedArray(
                        item.Attributes,
                        attribute => attribute.Id,
                        attribute => attribute.ComplexId)),
                new KeyValuePair<string, JsonNode?>("depth", ChannelCardSnapshot.ToNode(item.Depth)),
                new KeyValuePair<string, JsonNode?>("width", ChannelCardSnapshot.ToNode(item.Width)),
                new KeyValuePair<string, JsonNode?>("height", ChannelCardSnapshot.ToNode(item.Height)),
                new KeyValuePair<string, JsonNode?>("dimensionUnit", ChannelCardSnapshot.ToNode(item.DimensionUnit)),
                new KeyValuePair<string, JsonNode?>("weight", ChannelCardSnapshot.ToNode(item.Weight)),
                new KeyValuePair<string, JsonNode?>("weightUnit", ChannelCardSnapshot.ToNode(item.WeightUnit))
            },
            photos);
    }

    /// <summary>
    /// Both barcode shapes are present in live payloads and agree; the list wins when it has any
    /// entries, because a card may have several.
    /// </summary>
    private static List<string> MapBarcodes(OzonProductAttributesItem item)
    {
        List<string> barcodes = new List<string>();

        IEnumerable<string> source = item.Barcodes.Count > 0
            ? item.Barcodes
            : string.IsNullOrWhiteSpace(item.Barcode)
                ? Array.Empty<string>()
                : new[] { item.Barcode };

        foreach (string barcode in source)
        {
            if (!string.IsNullOrWhiteSpace(barcode))
            {
                barcodes.Add(barcode);
            }
        }

        return barcodes;
    }

    private static string? FindAttributeValue(OzonProductAttributesItem item, int attributeId)
    {
        foreach (OzonProductAttribute attribute in item.Attributes)
        {
            if (attribute.Id != attributeId)
            {
                continue;
            }

            foreach (OzonProductAttributeValue value in attribute.Values)
            {
                if (!string.IsNullOrWhiteSpace(value.Value))
                {
                    return value.Value;
                }
            }
        }

        return null;
    }
}
