using System.Text.Json;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Application.Orders;
using Delobytes.App.Backend.Integrations.Contracts.Models;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Ozon.Dtos;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Ozon;

/// <summary>
/// Ozon posting lists. Each call issues exactly one request for the requested schema; pagination is the
/// caller's, driven by <see cref="OrdersPage.NextCursor"/>.
/// </summary>
internal sealed class OzonOrdersClient
{
    private const string FboListPath = "/v3/posting/fbo/list";
    private const string FbsListPath = "/v4/posting/fbs/list";

    private readonly OzonRequestPipeline _pipeline;
    private readonly ILogger _logger;

    public OzonOrdersClient(OzonRequestPipeline pipeline, ILogger logger)
    {
        _pipeline = pipeline;
        _logger = logger;
    }

    /// <inheritdoc cref="IOrdersSource.GetOrdersAsync"/>
    ///
    /// <remarks>
    /// A null schema reads as FBO. Both schemas window on a single time range (FBO on
    /// <c>created_at</c>, FBS on <c>in_process_at</c> through the required <c>since</c>/<c>to</c> pair),
    /// so neither turns into a status refresh of postings imported earlier.
    /// <see cref="OrdersRequest.Selection"/> is ignored: Ozon has no last-change filter on these
    /// schemas that could replace the required window bounds.
    /// </remarks>
    public Task<OrdersPage> GetOrdersAsync(
        Guid connectionId, OrdersRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // A null schema reads as FBO: that is what a caller written before FBS existed, and one that
        // genuinely does not care which schema it gets, both mean.
        OrdersSchema effectiveSchema = request.Schema ?? OrdersSchema.Fbo;

        if (effectiveSchema != OrdersSchema.Fbo && effectiveSchema != OrdersSchema.Fbs)
        {
            throw new NotSupportedException($"Ozon schema '{effectiveSchema}' is not supported.");
        }

        if (request.DateFrom >= request.DateTo)
        {
            throw new ArgumentException(
                $"DateFrom ({request.DateFrom:O}) must be earlier than DateTo ({request.DateTo:O}).",
                nameof(request));
        }

        return effectiveSchema == OrdersSchema.Fbs
            ? GetFbsOrdersAsync(connectionId, request, cancellationToken)
            : GetFboOrdersAsync(connectionId, request, cancellationToken);
    }

    /// <summary>
    /// Issues exactly one <c>POST /v3/posting/fbo/list</c>.
    /// </summary>
    private Task<OrdersPage> GetFboOrdersAsync(
        Guid connectionId, OrdersRequest request, CancellationToken cancellationToken)
    {
        OzonPostingListRequest body =
            OzonRequestFactory.BuildFboBody(request, OzonRequestFactory.ClampPostingLimit(request.Limit));

        return FetchPostingListPageAsync(
            connectionId,
            FboListPath,
            OrdersRateLimiterMethods.OzonPostingFboList,
            OrdersSchema.Fbo,
            JsonSerializer.Serialize(body, OzonJson.Options),
            OzonResponseReader.Flat,
            cancellationToken);
    }

    /// <summary>
    /// Issues exactly one <c>POST /v4/posting/fbs/list</c>.
    /// </summary>
    private Task<OrdersPage> GetFbsOrdersAsync(
        Guid connectionId, OrdersRequest request, CancellationToken cancellationToken)
    {
        OzonFbsPostingListRequest body =
            OzonRequestFactory.BuildFbsBody(request, OzonRequestFactory.ClampPostingLimit(request.Limit));

        return FetchPostingListPageAsync(
            connectionId,
            FbsListPath,
            OrdersRateLimiterMethods.OzonPostingFbsList,
            OrdersSchema.Fbs,
            JsonSerializer.Serialize(body, OzonJson.Options),
            OzonResponseReader.Wrapped,
            cancellationToken);
    }

    /// <summary>
    /// Runs one posting list call end to end. Shared by both schemas because the only differences are
    /// the path, the limiter method, the request body shape and the reader, all of which the caller
    /// supplies. Keeping one implementation is what stops the two schemas from drifting apart in how
    /// they report throttling or turn a page into orders.
    /// </summary>
    /// <remarks>
    /// <c>sourceSchema</c> is passed on to <see cref="OzonOrderMapper.Map"/> so a posting that omits
    /// <c>delivery_schema</c> can still resolve <c>FulfillmentSchema</c> from the endpoint it was read
    /// from — <c>v3/posting/fbo/list</c> never reports the field at all.
    /// </remarks>
    private async Task<OrdersPage> FetchPostingListPageAsync(
        Guid connectionId,
        string path,
        string rateLimiterMethod,
        OrdersSchema sourceSchema,
        string body,
        Func<string, OzonPostingListResponse?> readPage,
        CancellationToken cancellationToken)
    {
        OzonSendResult<OzonPostingListResponse> result = await _pipeline.SendAsync(
            connectionId,
            new OzonRequest(path, rateLimiterMethod, body, SlotWaitTolerance: null),
            readPage,
            cancellationToken);

        if (result.SlotRefused || result.Throttled)
        {
            return CreateThrottledPage(result.RetryAt!.Value);
        }

        if (!result.IsSuccess)
        {
            throw new HttpRequestException(
                $"Ozon orders API returned {(int)result.StatusCode}.",
                null,
                result.StatusCode);
        }

        List<OzonPosting> postings = result.Body?.Postings ?? new List<OzonPosting>();

        OzonMappingResult mappingResult = OzonOrderMapper.Map(postings, sourceSchema, _logger);

        if (mappingResult.RowsWithoutIdentity > 0)
        {
            // Not thrown: the rest of the page is importable, and a whole run must not fail on rows the
            // channel itself sent without identity.
            _logger.LogWarning(
                "Ozon response for method {Method} contained {Count} posting(s) without identity.",
                rateLimiterMethod,
                mappingResult.RowsWithoutIdentity);
        }

        return new OrdersPage
        {
            Orders = mappingResult.Orders,
            HasMore = result.Body?.HasNext ?? false,
            NextCursor = result.Body?.Cursor,
            RowsWithoutIdentity = mappingResult.RowsWithoutIdentity,
        };
    }

    private static OrdersPage CreateThrottledPage(DateTimeOffset retryAt)
    {
        return new OrdersPage
        {
            Orders = new List<ChannelOrderSnapshot>(),
            HasMore = false,
            CoveredUntil = null,
            Throttled = true,
            RetryAt = retryAt,
            RowsWithoutIdentity = 0,
            NextCursor = null,
        };
    }
}
