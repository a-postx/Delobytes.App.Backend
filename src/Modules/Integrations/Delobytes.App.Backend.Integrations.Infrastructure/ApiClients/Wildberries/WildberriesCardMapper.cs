using System.Globalization;
using System.Text.Json;
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

        string? channelSpecificData = null;
        try
        {
            Dictionary<string, object?> additionalData = new Dictionary<string, object?>
            {
                ["imtId"] = dto.ImtId,
                ["nmUuid"] = dto.NmUuid,
                ["subjectId"] = dto.SubjectId,
                ["subjectName"] = dto.SubjectName,
                ["brand"] = dto.Brand,
                ["dimensions"] = dto.Dimensions,
                ["characteristics"] = dto.Characteristics,
                ["sizes"] = dto.Sizes,
                ["tags"] = dto.Tags,
                ["photos"] = dto.Photos,
                ["video"] = dto.Video,
                ["createdAt"] = dto.CreatedAt,
                ["updatedAt"] = dto.UpdatedAt
            };

            channelSpecificData = JsonSerializer.Serialize(additionalData);
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
            Photos = BuildPhotos(dto.Photos)
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
