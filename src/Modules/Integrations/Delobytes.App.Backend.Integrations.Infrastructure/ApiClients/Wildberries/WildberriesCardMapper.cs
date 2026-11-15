using System.Globalization;
using System.Text.Json.Nodes;
using Delobytes.App.Backend.Integrations.Application.DTOs;
using Delobytes.App.Backend.Integrations.Contracts.Models;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Wildberries.Dtos;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Wildberries;

/// <summary>
/// Maps one page of Wildberries <c>/content/v2/get/cards/list</c> into
/// <see cref="ProductCardsData"/>. Pure mapping: no HTTP, no orchestration, no DI.
/// </summary>
internal static class WildberriesCardMapper
{
    /// <summary>
    /// Maps a cards page, preserving the API's page order and cursor.
    /// </summary>
    /// <param name="response">Deserialized cards page.</param>
    /// <param name="logger">Logger for channel-specific data that failed to serialize; may be null.</param>
    /// <returns>The channel-neutral page.</returns>
    public static ProductCardsData Map(WildberriesGetCardsResponse response, ILogger? logger)
    {
        ArgumentNullException.ThrowIfNull(response);

        List<ChannelProductCard> cards = new List<ChannelProductCard>(response.Cards.Count);

        foreach (WildberriesCardDto card in response.Cards)
        {
            cards.Add(MapToCard(card, logger));
        }

        ProductCardsCursor? nextCursor = null;

        if (response.Cursor?.UpdatedAt != null && response.Cursor?.NmId != null)
        {
            nextCursor = new ProductCardsCursor
            {
                UpdatedAt = response.Cursor.UpdatedAt,
                ProductId = response.Cursor.NmId.Value
            };
        }

        return new ProductCardsData
        {
            Cards = cards,
            NextCursor = nextCursor,
            TotalCount = response.Cursor?.Total ?? cards.Count
        };
    }

    private static ChannelProductCard MapToCard(WildberriesCardDto dto, ILogger? logger)
    {
        List<string> barcodes = new List<string>();

        if (dto.Sizes != null)
        {
            foreach (WildberriesSize size in dto.Sizes)
            {
                if (size.Skus != null)
                {
                    barcodes.AddRange(size.Skus);
                }
            }
        }

        List<ChannelProductPhoto> photos = BuildPhotos(dto.Photos);

        string? channelSpecificData = null;
        try
        {
            // updatedAt is deliberately absent: Wildberries bumps it on its own, so carrying it here
            // marked every card of every re-import as changed. The raw photo block is absent for the
            // same reason — its variants carry a refresh marker — and the normalised gallery below
            // replaces it, so an actual gallery edit still reaches the comparison.
            channelSpecificData = ChannelCardSnapshot.Build(
                new List<KeyValuePair<string, JsonNode?>>
                {
                    new KeyValuePair<string, JsonNode?>("imtId", ChannelCardSnapshot.ToNode(dto.ImtId)),
                    new KeyValuePair<string, JsonNode?>("nmUuid", ChannelCardSnapshot.ToNode(dto.NmUuid)),
                    new KeyValuePair<string, JsonNode?>("subjectId", ChannelCardSnapshot.ToNode(dto.SubjectId)),
                    new KeyValuePair<string, JsonNode?>("subjectName", ChannelCardSnapshot.ToNode(dto.SubjectName)),
                    new KeyValuePair<string, JsonNode?>("brand", ChannelCardSnapshot.ToNode(dto.Brand)),
                    new KeyValuePair<string, JsonNode?>("dimensions", ChannelCardSnapshot.ToNode(dto.Dimensions)),
                    new KeyValuePair<string, JsonNode?>("characteristics", ChannelCardSnapshot.ToNode(dto.Characteristics)),
                    new KeyValuePair<string, JsonNode?>("sizes", ChannelCardSnapshot.ToNode(dto.Sizes)),
                    new KeyValuePair<string, JsonNode?>("tags", ChannelCardSnapshot.ToNode(dto.Tags)),
                    new KeyValuePair<string, JsonNode?>("video", ChannelCardSnapshot.ToNode(dto.Video)),
                    new KeyValuePair<string, JsonNode?>("createdAt", ChannelCardSnapshot.ToNode(dto.CreatedAt))
                },
                photos);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to serialize channel-specific data for nmID {NmId}.", dto.NmId);
        }

        return new ChannelProductCard
        {
            ExternalProductId = dto.NmId.ToString(CultureInfo.InvariantCulture),
            OfferId = dto.VendorCode ?? string.Empty,
            ChannelCode = ChannelCodes.Wildberries,
            BarcodeType = ChannelCodes.Wildberries,
            Name = dto.Title ?? string.Empty,
            Description = dto.Description,
            Barcodes = barcodes,
            LengthCm = dto.Dimensions?.Length,
            WidthCm = dto.Dimensions?.Width,
            HeightCm = dto.Dimensions?.Height,
            WeightKg = dto.Dimensions?.WeightBrutto,
            ChannelSpecificData = channelSpecificData,
            Photos = photos
        };
    }

    /// <summary>
    /// Expands the two variants the importer downloads into neutral photo entries, keeping the
    /// gallery order: both variants of one photo share its display order.
    /// </summary>
    private static List<ChannelProductPhoto> BuildPhotos(List<WildberriesPhoto>? photos)
    {
        List<ChannelProductPhoto> result = new List<ChannelProductPhoto>();

        if (photos == null)
        {
            return result;
        }

        int displayOrder = 1;

        foreach (WildberriesPhoto photo in photos)
        {
            AddPhoto(result, photo.C246x328, "thumbnail", 246, 328, displayOrder);
            AddPhoto(result, photo.C516x688, "large", 516, 688, displayOrder);

            displayOrder++;
        }

        return result;
    }

    private static void AddPhoto(
        List<ChannelProductPhoto> photos,
        string? url,
        string sizeVariant,
        int width,
        int height,
        int displayOrder)
    {
        if (string.IsNullOrEmpty(url))
        {
            return;
        }

        photos.Add(new ChannelProductPhoto
        {
            Url = url,
            SizeVariant = sizeVariant,
            Width = width,
            Height = height,
            DisplayOrder = displayOrder
        });
    }
}
