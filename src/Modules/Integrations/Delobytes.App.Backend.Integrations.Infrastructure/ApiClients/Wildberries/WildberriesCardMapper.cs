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

        List<WildberriesCardSnapshot> snapshots = new List<WildberriesCardSnapshot>(response.Cards.Count);

        foreach (WildberriesCardDto card in response.Cards)
        {
            snapshots.Add(MapToSnapshot(card, logger));
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
            Cards = snapshots,
            NextCursor = nextCursor,
            TotalCount = response.Cursor?.Total ?? snapshots.Count
        };
    }

    private static WildberriesCardSnapshot MapToSnapshot(WildberriesCardDto dto, ILogger? logger)
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

        return new WildberriesCardSnapshot
        {
            NmId = dto.NmId,
            Name = dto.Title ?? string.Empty,
            VendorCode = dto.VendorCode ?? string.Empty,
            Barcodes = barcodes,
            Description = dto.Description,
            LengthCm = dto.Dimensions?.Length,
            WidthCm = dto.Dimensions?.Width,
            HeightCm = dto.Dimensions?.Height,
            WeightKg = dto.Dimensions?.WeightBrutto,
            ChannelSpecificData = channelSpecificData,
            Photos = dto.Photos?
                .Select(p => new WildberriesPhotoUrls { C246x328 = p.C246x328, C516x688 = p.C516x688 })
                .ToList() ?? new List<WildberriesPhotoUrls>()
        };
    }
}
