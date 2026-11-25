using System.Net;
using System.Text.Json;
using Delobytes.App.Backend.Integrations.Application.DTOs;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Contracts.Models;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Ozon.Dtos;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Ozon;

/// <summary>
/// Ozon product cards, read through the attributes endpoint: one request is both the listing and the
/// card payload.
/// </summary>
internal sealed class OzonProductsClient
{
    private const string AttributesPath = "/v4/product/info/attributes";

    /// <summary>Ozon's own ceiling for one attributes page.</summary>
    private const int PageSizeMaximum = 1000;

    /// <summary>
    /// Longest in-process wait the products client accepts for a rate-limiter slot. With a two-second
    /// minimum interval the wait never approaches this; the bound only stops a broken limiter from
    /// hanging a run.
    /// </summary>
    private static readonly TimeSpan SlotWaitTolerance = TimeSpan.FromSeconds(30);

    private readonly OzonRequestPipeline _pipeline;
    private readonly ILogger _logger;
    private readonly TimeProvider _timeProvider;

    public OzonProductsClient(OzonRequestPipeline pipeline, ILogger logger, TimeProvider timeProvider)
    {
        _pipeline = pipeline;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc cref="IChannelApiClient.GetProductCardsAsync"/>
    public async Task<ApiResponse<ProductCardsData>> GetProductCardsAsync(
        Guid connectionId, ProductCardsCursor? cursor, int limit, CancellationToken cancellationToken)
    {
        int pageSize = Math.Clamp(limit, 1, PageSizeMaximum);

        OzonProductAttributesRequest body =
            OzonRequestFactory.BuildProductAttributesBody(pageSize, cursor?.Token);

        OzonSendResult<OzonProductAttributesResponse> result = await _pipeline.SendAsync(
            connectionId,
            new OzonRequest(
                AttributesPath,
                AttributesPath,
                JsonSerializer.Serialize(body, OzonJson.Options),
                SlotWaitTolerance),
            payload => JsonSerializer.Deserialize<OzonProductAttributesResponse>(payload, OzonJson.Options),
            cancellationToken);

        // The orchestrator treats any non-success page as a failed job, so a short wait is always
        // better than a refusal: the failure is reported only when the slot is beyond the tolerance.
        if (result.SlotRefused)
        {
            DateTimeOffset refusedUntil = result.RetryAt!.Value;

            return new ApiResponse<ProductCardsData>
            {
                IsSuccess = false,
                ErrorMessage = $"Rate limit: slot not available until {refusedUntil:O}.",
                StatusCode = (int)result.StatusCode,
                RetryAt = refusedUntil,
                Timestamp = _timeProvider.GetUtcNow(),
                Data = new ProductCardsData(),
            };
        }

        if (result.Throttled)
        {
            DateTimeOffset retryAt = result.RetryAt!.Value;

            return new ApiResponse<ProductCardsData>
            {
                IsSuccess = false,
                ErrorMessage = $"Rate limit exceeded. Retry after {retryAt:O}.",
                StatusCode = (int)result.StatusCode,
                RetryAt = retryAt,
                Timestamp = _timeProvider.GetUtcNow(),
                Data = new ProductCardsData(),
            };
        }

        if (!result.IsSuccess)
        {
            throw new HttpRequestException(
                $"Ozon products API returned {(int)result.StatusCode}.",
                null,
                result.StatusCode);
        }

        List<OzonProductAttributesItem> resultItems =
            result.Body?.Result ?? new List<OzonProductAttributesItem>();

        List<ChannelProductCard> cards = new List<ChannelProductCard>();

        foreach (OzonProductAttributesItem item in resultItems)
        {
            if (item.Sku == 0)
            {
                // No marketplace article: two such cards would collide on the value "0" inside the
                // unique index that keys links by (channel, external product id).
                _logger.LogWarning(
                    "Ozon card Id={OzonId}, OfferId={OfferId} has no sku; skipping it.",
                    item.Id,
                    item.OfferId);

                continue;
            }

            cards.Add(OzonCardMapper.Map(item));
        }

        return new ApiResponse<ProductCardsData>
        {
            IsSuccess = true,
            StatusCode = (int)HttpStatusCode.OK,
            Timestamp = _timeProvider.GetUtcNow(),
            Data = new ProductCardsData
            {
                Cards = cards,
                NextCursor = HasMorePages(resultItems.Count, pageSize, result.Body?.LastId)
                    ? new ProductCardsCursor { Token = result.Body!.LastId }
                    : null,
                TotalCount = result.Body?.Total ?? 0,
            },
        };
    }

    /// <inheritdoc cref="IProductsSource.GetProductCardsAsync"/>
    public async Task<ProductCardsPage> GetProductCardsAsync(
        Guid connectionId, ProductCardsPageRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        ApiResponse<ProductCardsData> response =
            await GetProductCardsAsync(connectionId, request.Cursor, request.Limit, cancellationToken);

        if (!response.IsSuccess)
        {
            throw new InvalidOperationException(
                $"Failed to retrieve Ozon product cards: {response.ErrorMessage}");
        }

        return new ProductCardsPage
        {
            Cards = response.Data?.Cards.ToList() ?? new List<ChannelProductCard>(),
            NextCursor = response.Data?.NextCursor,
            TotalCount = response.Data?.TotalCount ?? 0,
        };
    }

    /// <summary>
    /// Decides whether the page just read is followed by another one.
    /// </summary>
    /// <remarks>
    /// Ozon fills <c>last_id</c> on every page, the terminal one included: a catalogue of 95 cards read
    /// with <c>limit: 100</c> comes back complete, with a populated <c>last_id</c>. Treating a
    /// non-empty value as "there is more" makes the run request a page that does not exist, which the
    /// channel answers with 404 <c>{"code":5,"message":"item not found"}</c> and fails the job. A page
    /// shorter than the requested limit therefore ends the catalogue, and <c>last_id</c> decides only
    /// when the page is full.
    /// </remarks>
    /// <param name="returnedItems">
    /// Items the channel returned, counted before cards without a sku are dropped: a full page that
    /// loses a few rows in mapping must still advance the cursor.
    /// </param>
    /// <param name="requestedLimit">Page size the page was requested with.</param>
    /// <param name="lastId">Cursor of the page, empty when the channel sent none.</param>
    /// <returns>True when another request may be issued; false when the catalogue is exhausted.</returns>
    private static bool HasMorePages(int returnedItems, int requestedLimit, string? lastId)
    {
        if (string.IsNullOrEmpty(lastId))
        {
            return false;
        }

        return returnedItems >= requestedLimit;
    }
}
