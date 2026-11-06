using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Delobytes.App.Backend.Integrations.Application.DTOs;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Contracts.Models;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Ozon;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Ozon.Dtos;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Integrations.Infrastructure.ApiClients;

/// <summary>
/// Ozon marketplace API client. Reads FBO and FBS orders; product cards are not supported yet.
/// </summary>
public class OzonApiClient : IChannelApiClient, IProductsSource, IOrdersSource
{
    private const string DefaultApiBaseUrl = "https://api-seller.ozon.ru";
    private const string OzonChannelCode = "ozon";
    private const string FboListPath = "/v3/posting/fbo/list";
    private const string FbsListPath = "/v4/posting/fbs/list";

    // Both posting list endpoints share the same page size envelope.
    private const int PostingListPageSizeMinimum = 1;
    private const int PostingListPageSizeMaximum = 100;
    private const int PostingListDefaultPageSize = 100;
    private const int ResponseBodyLogLimit = 500;

    private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;

    // только для метода GetAccountInfoAsync
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConnectionResolver _connectionResolver;
    private readonly IOrdersRateLimiter _ordersRateLimiter;
    private readonly ILogger<OzonApiClient> _logger;

    private Guid? _templateId;
    private string? _apiBaseUrl;

    /// <inheritdoc/>
    public string ChannelCode => OzonChannelCode;

    /// <summary>
    /// Gets the base URL of the bound system channel template, falling back to the Ozon public
    /// endpoint. Exposed for tests asserting that the factory binds the template.
    /// </summary>
    internal string BaseUrl => _apiBaseUrl ?? DefaultApiBaseUrl;

    /// <summary>
    /// Gets the identifier of the bound system channel template, or null when the client was not
    /// resolved through the factory.
    /// </summary>
    internal Guid? TemplateId => _templateId;

    /// <summary>
    /// Initializes a new instance of the <see cref="OzonApiClient"/> class.
    /// </summary>
    /// <param name="httpClient">Typed HTTP client carrying <see cref="OzonAuthHandler"/>.</param>
    /// <param name="httpClientFactory">Factory used by <see cref="GetAccountInfoAsync"/> only.</param>
    /// <param name="connectionResolver">
    /// Resolves the connection the request authenticates as, so the rate-limit scope is keyed by the
    /// key Ozon actually meters rather than by an arbitrary connection of the tenant.
    /// </param>
    /// <param name="ordersRateLimiter">Channel-neutral orders pacing.</param>
    /// <param name="logger">Logger instance.</param>
    public OzonApiClient(
        HttpClient httpClient,
        IHttpClientFactory httpClientFactory,
        IConnectionResolver connectionResolver,
        IOrdersRateLimiter ordersRateLimiter,
        ILogger<OzonApiClient> logger)
    {
        _httpClient = httpClient;
        _httpClientFactory = httpClientFactory;
        _connectionResolver = connectionResolver;
        _ordersRateLimiter = ordersRateLimiter;
        _logger = logger;
    }

    /// <summary>
    /// Binds the client to a system channel template. Called by
    /// <see cref="ChannelApiClientFactory"/>, never by the calling code.
    /// </summary>
    /// <param name="template">Channel template of the Ozon system channel.</param>
    public void SetTemplate(SystemChannelTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);

        _templateId = template.Id;
        _apiBaseUrl = string.IsNullOrWhiteSpace(template.ApiBaseUrl)
            ? DefaultApiBaseUrl
            : template.ApiBaseUrl;
    }

    /// <inheritdoc/>
    public async Task<AccountInfo?> GetAccountInfoAsync(
        Guid? connectionId,
        string apiKey,
        string? apiSecret,
        Dictionary<string, string>? settings,
        CancellationToken ct)
    {
        if (settings == null || !settings.TryGetValue("sellerId", out string? sellerId))
        {
            _logger.LogWarning("Cannot retrieve Ozon account info: sellerId is missing.");
            return null;
        }

        using HttpClient client = _httpClientFactory.CreateClient();

        using HttpRequestMessage request = new HttpRequestMessage(
            HttpMethod.Post,
            "https://api-seller.ozon.ru/v1/seller/info");

        request.Headers.TryAddWithoutValidation("Client-Id", sellerId);
        request.Headers.TryAddWithoutValidation("Api-Key", apiKey);
        request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

        try
        {
            using HttpResponseMessage response = await client.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Ozon seller/info returned {StatusCode}.", (int)response.StatusCode);
                return null;
            }

            OzonSellerInfoResponse? body =
                await response.Content.ReadFromJsonAsync<OzonSellerInfoResponse>(ct);

            if (body is null)
            {
                return null;
            }

            return new AccountInfo
            {
                CustomerName = body.Company?.Name,
                LegalName = body.Company?.LegalName,
                Inn = body.Company?.Inn,
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Failed to retrieve Ozon seller info.");
            return null;
        }
    }

    /// <inheritdoc cref="IOrdersSource.GetOrdersAsync"/>
    ///
    /// <remarks>
    /// Issues exactly one posting list call for the requested schema and returns the page the
    /// response describes: pagination is the caller's, driven by <see cref="OrdersPage.NextCursor"/>.
    /// A null schema reads as FBO. Both schemas window on a single time range (FBO on
    /// <c>created_at</c>, FBS on <c>in_process_at</c> through the required <c>since</c>/<c>to</c>
    /// pair), so neither turns into a status refresh of postings imported earlier.
    /// <see cref="OrdersRequest.Selection"/> is ignored: Ozon has no last-change filter on these
    /// schemas that could replace the required window bounds.
    /// </remarks>
    public Task<OrdersPage> GetOrdersAsync(
        Guid connectionId, OrdersRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // A null schema reads as FBO: that is what a caller written before FBS existed, and one
        // that genuinely does not care which schema it gets, both mean.
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

        if (_templateId == null)
        {
            throw new InvalidOperationException(
                "Template must be bound before calling API methods. Obtain the client from IChannelApiClientFactory.");
        }

        return effectiveSchema == OrdersSchema.Fbs
            ? GetFbsOrdersAsync(connectionId, request, cancellationToken)
            : GetFboOrdersAsync(connectionId, request, cancellationToken);
    }

    /// <summary>
    /// Reads a page of the flat <c>v3/posting/fbo/list</c> shape, where the page members sit at the
    /// top level of the body.
    /// </summary>
    private static Task<OzonPostingListResponse?> ReadFlatPageAsync(
        HttpContent content, CancellationToken cancellationToken)
    {
        return content.ReadFromJsonAsync<OzonPostingListResponse>(cancellationToken);
    }

    /// <summary>
    /// Reads a page of the <c>v4/posting/fbs/list</c> body. Ozon documents the page wrapped in a
    /// <c>result</c> object while FBO returns it flat, and reading one shape as the other yields an
    /// empty page without raising - an import that silently finds no orders. Both shapes are
    /// therefore accepted, decided by the presence of <c>result</c> rather than by trying one and
    /// falling back, because a missing <c>postings</c> member is indistinguishable from a genuinely
    /// empty page.
    /// </summary>
    private static async Task<OzonPostingListResponse?> ReadFbsPageAsync(
        HttpContent content, CancellationToken cancellationToken)
    {
        string body = await content.ReadAsStringAsync(cancellationToken);

        using JsonDocument document = JsonDocument.Parse(body);

        JsonElement root = document.RootElement;

        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("result", out JsonElement result)
            && result.ValueKind == JsonValueKind.Object)
        {
            return result.Deserialize<OzonPostingListResponse>(SerializerOptions);
        }

        return root.Deserialize<OzonPostingListResponse>(SerializerOptions);
    }

    /// <summary>
    /// Issues exactly one <c>POST /v3/posting/fbo/list</c>.
    /// </summary>
    private Task<OrdersPage> GetFboOrdersAsync(
        Guid connectionId, OrdersRequest request, CancellationToken cancellationToken)
    {
        OzonPostingListRequest body = BuildRequestBody(request, ClampLimit(request.Limit));

        return FetchPostingListPageAsync(
            connectionId,
            FboListPath,
            OrdersRateLimiterMethods.OzonPostingFboList,
            OrdersSchema.Fbo,
            body,
            ReadFlatPageAsync,
            cancellationToken);
    }

    /// <summary>
    /// Issues exactly one <c>POST /v4/posting/fbs/list</c>.
    /// </summary>
    private Task<OrdersPage> GetFbsOrdersAsync(
        Guid connectionId, OrdersRequest request, CancellationToken cancellationToken)
    {
        OzonFbsPostingListRequest body = BuildFbsRequestBody(request, ClampLimit(request.Limit));

        return FetchPostingListPageAsync(
            connectionId,
            FbsListPath,
            OrdersRateLimiterMethods.OzonPostingFbsList,
            OrdersSchema.Fbs,
            body,
            ReadFbsPageAsync,
            cancellationToken);
    }

    /// <summary>
    /// Runs one posting list call end to end: rate limiter admission, the single POST, throttling and
    /// failure handling, then mapping. Shared by both schemas because the only differences between
    /// them are the path, the limiter method and the request body shape - all of which the caller
    /// supplies, plus the reader for the response shape, which v3 and v4 disagree on. Keeping one
    /// implementation is what stops the two schemas from drifting apart in how they report throttling
    /// or turn a page into orders.
    /// <paramref name="sourceSchema"/> is passed on to <see cref="OzonOrderMapper.Map"/> so a
    /// posting that omits <c>delivery_schema</c> can still resolve <c>FulfillmentSchema</c> from the
    /// endpoint it was read from - <c>v3/posting/fbo/list</c> never reports the field at all.
    /// </summary>
    private async Task<OrdersPage> FetchPostingListPageAsync<TBody>(
        Guid connectionId,
        string path,
        string rateLimiterMethod,
        OrdersSchema sourceSchema,
        TBody body,
        Func<HttpContent, CancellationToken, Task<OzonPostingListResponse?>> readPage,
        CancellationToken cancellationToken)
    {
        Connection connection = await _connectionResolver.ResolveByIdAsync(connectionId, cancellationToken);

        RateLimitScope scope = new RateLimitScope(OzonChannelCode, connection.ApiKey, rateLimiterMethod);

        RateLimitAcquireResult acquireResult = await _ordersRateLimiter.TryAcquireAsync(scope, cancellationToken);

        if (!acquireResult.Acquired)
        {
            // Not a failure and not an empty page: the caller keeps its cursor and returns later.
            _logger.LogInformation(
                "Ozon orders request for connection {ConnectionId} was not issued: the rate limiter " +
                "has not released the next slot for method {Method} yet.",
                connectionId,
                scope.Method);

            return CreateThrottledPage(acquireResult.NextRequestAllowedAt);
        }

        using HttpRequestMessage httpRequest = new HttpRequestMessage(
            HttpMethod.Post, $"{BaseUrl}{path}")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(body, SerializerOptions), Encoding.UTF8, "application/json"),
        };

        httpRequest.Options.Set(OzonAuthHandler.ConnectionIdOptionKey, connectionId);

        using HttpResponseMessage response = await _httpClient.SendAsync(httpRequest, cancellationToken);

        RateLimitSignal rateLimitSignal = OzonRateLimitHeaders.Parse(response);

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            DateTimeOffset retryAt = await _ordersRateLimiter.ReportRateLimitedAsync(
                scope, rateLimitSignal, cancellationToken);

            string throttledBody = await response.Content.ReadAsStringAsync(cancellationToken);

            _logger.LogWarning(
                "Ozon orders API throttled the request (429): connection {ConnectionId}, method " +
                "{Method}, x-request-id {RequestId}, Ratelimit-Remaining {Remaining}, " +
                "Retry-After {RetrySeconds}s. Response: {Response}",
                connectionId,
                scope.Method,
                rateLimitSignal.RequestId,
                rateLimitSignal.Remaining,
                rateLimitSignal.RetrySeconds,
                TruncateForLog(throttledBody));

            return CreateThrottledPage(retryAt);
        }

        if (!response.IsSuccessStatusCode)
        {
            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            _logger.LogWarning(
                "Ozon orders API returned {StatusCode} for method {Method} (x-request-id {RequestId}). " +
                "Response: {Response}",
                (int)response.StatusCode,
                scope.Method,
                rateLimitSignal.RequestId,
                TruncateForLog(responseBody));

            throw new HttpRequestException(
                $"Ozon orders API returned {(int)response.StatusCode}.",
                null,
                response.StatusCode);
        }

        // Reported before the body is read: the counters describe this response, and a body that
        // fails to deserialize must not cost the limiter its only feedback on the real allowance.
        await _ordersRateLimiter.ReportSuccessAsync(scope, rateLimitSignal, cancellationToken);

        OzonPostingListResponse? page = await readPage(response.Content, cancellationToken);

        List<OzonPosting> postings = page?.Postings ?? new List<OzonPosting>();

        List<ChannelOrderSnapshot> orders = OzonOrderMapper.Map(postings, sourceSchema, _logger, out int rowsWithoutIdentity);

        if (rowsWithoutIdentity > 0)
        {
            // Not thrown: the rest of the page is importable, and a whole run must not fail on rows
            // the channel itself sent without identity.
            _logger.LogWarning(
                "Ozon response for method {Method} contained {Count} posting(s) without identity.",
                scope.Method,
                rowsWithoutIdentity);
        }

        return new OrdersPage
        {
            Orders = orders,
            HasMore = page?.HasNext ?? false,
            NextCursor = page?.Cursor,
            RowsWithoutIdentity = rowsWithoutIdentity,
        };
    }

    public Task<ApiResponse<ProductCardsData>> GetProductCardsAsync(
        Guid connectionId, ProductCardsCursor? cursor, int limit, CancellationToken ct)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc/>
    ///
    /// <remarks>Ozon does not support product cards sync yet.</remarks>
    public Task<ProductCardsPage> GetProductCardsAsync(
        Guid connectionId, ProductCardsPageRequest request, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Ozon does not support product cards sync yet.");
    }

    private static int ClampLimit(int limit)
    {
        if (limit <= 0)
        {
            return PostingListDefaultPageSize;
        }

        return Math.Clamp(limit, PostingListPageSizeMinimum, PostingListPageSizeMaximum);
    }

    private static OzonPostingListRequest BuildRequestBody(OrdersRequest request, int limit)
    {
        return new OzonPostingListRequest
        {
            Cursor = request.Cursor ?? string.Empty,
            Filter = new OzonPostingListFilter
            {
                Since = FormatUtc(request.DateFrom),
                To = FormatUtc(request.DateTo),
                Statuses = request.Statuses is { Count: > 0 }
                    ? new List<string>(request.Statuses)
                    : new List<string>(OzonPostingStatuses.FboStatuses),
            },
            Limit = limit,
            SortDir = "ASC",
            Translit = false,
            With = new OzonPostingListWith
            {
                AnalyticsData = true,
                FinancialData = true,
                LegalInfo = false,
            },
        };
    }

    /// <summary>
    /// Builds the FBS <c>posting/fbs/list</c> body. Separate from <see cref="BuildRequestBody"/>
    /// because the endpoint takes a different DTO: its filter carries the required
    /// <c>since</c>/<c>to</c> pair and its <c>with</c> block has no analytics section.
    /// </summary>
    private static OzonFbsPostingListRequest BuildFbsRequestBody(OrdersRequest request, int limit)
    {
        return new OzonFbsPostingListRequest
        {
            Cursor = request.Cursor ?? string.Empty,
            Filter = new OzonFbsPostingListFilter
            {
                Since = FormatUtc(request.DateFrom),
                To = FormatUtc(request.DateTo),

                // The full set is sent rather than null: the filter member is declared nullable, so a
                // null would serialize as an explicit "statuses": null instead of being omitted, and
                // the endpoint is only documented to drop the filter when the member is absent.
                Statuses = request.Statuses is { Count: > 0 }
                    ? new List<string>(request.Statuses)
                    : new List<string>(OzonPostingStatuses.FbsStatuses),
            },
            Limit = limit,
            SortDir = "asc",
            With = new OzonFbsPostingListWith
            {
                FinancialData = true,
            },
        };
    }

    /// <summary>
    /// Formats an instant the way Ozon documents it: UTC with a <c>Z</c> designator. The offset form
    /// the serializer would otherwise emit is rejected by the API.
    /// </summary>
    private static string FormatUtc(DateTimeOffset value)
    {
        return value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
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

    private static string TruncateForLog(string responseBody)
    {
        if (string.IsNullOrEmpty(responseBody) || responseBody.Length <= ResponseBodyLogLimit)
        {
            return responseBody;
        }

        return responseBody.Substring(0, ResponseBodyLogLimit);
    }

    private sealed class OzonSellerInfoResponse
    {
        [JsonPropertyName("company")]
        public OzonCompanyInfo? Company { get; init; }
    }

    private sealed class OzonCompanyInfo
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("legal_name")]
        public string? LegalName { get; init; }

        [JsonPropertyName("inn")]
        public string? Inn { get; init; }
    }
}
