using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Delobytes.App.Backend.Integrations.Application.DTOs;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Application.Options;
using Delobytes.App.Backend.Integrations.Application.Orders;
using Delobytes.App.Backend.Integrations.Application.SalesChannels;
using Delobytes.App.Backend.Integrations.Contracts.Models;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.RateLimiting;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Wildberries.Dtos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Wildberries;

/// <summary>
/// Wildberries marketplace API client implementation. Owns transport concerns only: endpoint
/// resolution, authentication, pacing, retries and response status handling. Turning a response
/// into snapshots belongs to <see cref="WildberriesOrderMapper"/> and
/// <see cref="WildberriesCardMapper"/>.
/// </summary>
public class WildberriesApiClient : IChannelApiClient, IProductsSource, IOrdersSource, IChannelApiClientTemplateBinder
{
    private const int DeserializationMaxAttempts = 3;
    private const int ResponseBodyLogLimit = 500;

    private static readonly TimeSpan DefaultDeserializationRetryDelay = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Wildberries has operated on a fixed UTC+3 offset (no DST) since 2014; the orders API
    /// takes dates in this local time and does not accept a time zone designator.
    /// </summary>
    private static readonly TimeSpan MoscowOffset = OrdersWindowPlanner.MoscowTime;

    private readonly HttpClient _httpClient;
    private readonly IEndpointResolver _endpointResolver;
    private readonly IConnectionResolver _connectionResolver;
    private readonly IChannelRateLimiter _rateLimiter;
    private readonly ILogger<WildberriesApiClient> _logger;
    private readonly WildberriesImportOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly RateLimitRequestPacer _pacer;

    private Guid? _templateId;

    private string? _channelCode;

    /// <summary>
    /// Base delay before retrying a response body that failed to deserialize. Exposed as a
    /// settable property rather than a constructor parameter so the container keeps a single
    /// unambiguous constructor; tests shorten it to keep retry assertions fast.
    /// </summary>
    internal TimeSpan DeserializationRetryDelay { get; set; } = DefaultDeserializationRetryDelay;

    /// <inheritdoc/>
    public string ChannelCode
    {
        get
        {
            return _channelCode ?? throw new InvalidOperationException(
            "Template must be bound before reading ChannelCode. Obtain the client from IChannelApiClientFactory.");
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WildberriesApiClient"/> class.
    /// </summary>
    public WildberriesApiClient(
        HttpClient httpClient,
        IEndpointResolver endpointResolver,
        IConnectionResolver connectionResolver,
        [FromKeyedServices(ChannelCatalog.WildberriesCode)] IChannelRateLimiter rateLimiter,
        IOptions<WildberriesImportOptions> options,
        ILogger<WildberriesApiClient> logger,
        TimeProvider timeProvider)
    {
        _httpClient = httpClient;
        _endpointResolver = endpointResolver;
        _connectionResolver = connectionResolver;
        _rateLimiter = rateLimiter;
        _options = options.Value;
        _logger = logger;
        _timeProvider = timeProvider;
        _pacer = new RateLimitRequestPacer(rateLimiter, timeProvider, logger);
    }

    /// <summary>
    /// Binds this client instance to a system channel template.
    /// Both the template identity and the channel code are written here and nowhere else, so
    /// they cannot disagree.
    /// </summary>
    /// <param name="template">System channel template whose endpoints should be used.</param>
    public void SetTemplate(SystemChannelTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);

        _templateId = template.Id;
        _channelCode = template.Code;
    }

    /// <inheritdoc/>
    public async Task<AccountInfo?> GetAccountInfoAsync(
        Guid? connectionId,
        string apiKey,
        string? apiSecret,
        Dictionary<string, string>? settings,
        CancellationToken ct)
    {
        if (_templateId == null)
        {
            throw new InvalidOperationException(
                "Template must be bound before calling API methods. Obtain the client from IChannelApiClientFactory.");
        }

        string baseUrl = await _endpointResolver.GetEndpointUrlAsync(
            _templateId.Value,
            ChannelEndpointType.Common,
            ct);

        // The explicit header below is what the request actually authenticates with, so it is also
        // what the limiter must pace on: resolving the persisted Connection here would pace a
        // different key whenever the caller validates a candidate key that is not stored yet.
        RateLimitScope scope = new RateLimitScope(
            ChannelCode,
            apiKey,
            WildberriesApiMethodRegistry.SellerInfo);

        RateLimitAcquireResult acquireResult = await _rateLimiter.TryAcquireAsync(scope, ct);

        if (!acquireResult.Acquired)
        {
            // Account info is optional context, so a deferred call reports "unknown" rather than
            // burning a request the shared key budget has not released.
            _logger.LogInformation(
                "Wildberries seller-info request was not issued: the rate limiter has not released " +
                "the next slot for method {Method} until {NextAllowed:O}.",
                scope.Method,
                acquireResult.NextRequestAllowedAt);

            return null;
        }

        using HttpRequestMessage request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{baseUrl}/api/v1/seller-info");

        // Explicit header takes precedence over WildberriesAuthHandler's own Connection
        // resolution: the key being validated here may not belong to any persisted Connection
        // yet (new connection setup).
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");

        if (connectionId.HasValue)
        {
            request.Options.Set(WildberriesAuthHandler.ConnectionIdOptionKey, connectionId.Value);
        }

        try
        {
            using HttpResponseMessage response = await _httpClient.SendAsync(request, ct);

            RateLimitSignal rateLimitSignal = WildberriesRateLimitHeaders.Parse(response);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                DateTimeOffset retryAt = await _rateLimiter.ReportRateLimitedAsync(
                    scope, rateLimitSignal, ct);

                _logger.LogWarning(
                    "Wildberries seller-info was throttled (429, x-request-id {RequestId}); " +
                    "the next attempt is not due before {NextAttempt:O}.",
                    rateLimitSignal.RequestId,
                    retryAt);

                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Wildberries seller-info returned {StatusCode} (x-request-id {RequestId}).",
                    (int)response.StatusCode,
                    rateLimitSignal.RequestId);
                return null;
            }

            // Reported before the body is read: the counters describe this response, and a body
            // that fails to deserialize must not cost the limiter its only feedback on the allowance.
            await _rateLimiter.ReportSuccessAsync(scope, rateLimitSignal, ct);

            WildberriesSellerInfoResponse? body =
                await response.Content.ReadFromJsonAsync<WildberriesSellerInfoResponse>(ct);

            if (body is null)
            {
                return null;
            }

            return new AccountInfo
            {
                CustomerName = body.Trademark,
                LegalName = body.Name,
                Inn = body.Tin,
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Failed to retrieve Wildberries seller info.");
            return null;
        }
    }

    /// <inheritdoc cref="IProductsSource.GetProductCardsAsync"/>
    ///
    /// <remarks>
    /// Thin adapter over the cursor/limit overload below: the capability interface carries no
    /// success/failure flag of its own, so a non-success <see cref="ApiResponse{T}"/> is raised
    /// as an exception instead of being returned silently.
    /// </remarks>
    public async Task<ProductCardsPage> GetProductCardsAsync(
        Guid connectionId, ProductCardsPageRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        ApiResponse<ProductCardsData> response =
            await GetProductCardsAsync(connectionId, request.Cursor, request.Limit, cancellationToken);

        if (!response.IsSuccess)
        {
            throw new InvalidOperationException(
                $"Failed to retrieve Wildberries product cards: {response.ErrorMessage}");
        }

        return new ProductCardsPage
        {
            Cards = response.Data?.Cards.ToList() ?? new List<ChannelProductCard>(),
            NextCursor = response.Data?.NextCursor,
            TotalCount = response.Data?.TotalCount ?? 0
        };
    }

    /// <inheritdoc/>
    public async Task<ApiResponse<ProductCardsData>> GetProductCardsAsync(
        Guid connectionId,
        ProductCardsCursor? cursor,
        int limit,
        CancellationToken ct)
    {
        if (_templateId == null)
        {
            throw new InvalidOperationException(
                "Template must be bound before calling API methods. Obtain the client from IChannelApiClientFactory.");
        }

        if (limit < 1 || limit > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be between 1 and 100.");
        }

        string baseUrl = await _endpointResolver.GetEndpointUrlAsync(
            _templateId.Value,
            ChannelEndpointType.Content,
            ct);

        WildberriesGetCardsRequest requestBody = new WildberriesGetCardsRequest
        {
            Settings = new WildberriesCardSettings
            {
                Cursor = new WildberriesCursorRequest
                {
                    Limit = limit,
                    UpdatedAt = cursor?.UpdatedAt,
                    NmId = cursor?.ProductId
                },
                Filter = new WildberriesCardFilter()
            },
            Filter = new WildberriesCardFilter()
        };

        string requestJson = JsonSerializer.Serialize(requestBody, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });

        string requestUrl = $"{baseUrl}/content/v2/get/cards/list";

        Connection connection = await _connectionResolver.ResolveByIdAsync(connectionId, ct);

        RateLimitScope scope = new RateLimitScope(
            ChannelCode,
            connection.ApiKey,
            WildberriesApiMethodRegistry.ContentCardsList);

        for (int attempt = 1; ; attempt++)
        {
            try
            {
                // The products import has no continuation of its own: it walks a cursor page by page
                // inside a single run, so a refusal it cannot wait out ends the run. Waiting here is
                // what keeps a multi-page import alive under the same adaptive budget the orders
                // path respects.
                TimeSpan tolerance = TimeSpan.FromSeconds(Math.Max(0, _options.MaxInProcessWaitSeconds));

                RateLimitAcquireResult? refusedSlot =
                    await _pacer.WaitForSlotAsync(scope, tolerance, ct);

                if (refusedSlot is not null)
                {
                    return CreateDeferredCardsResponse(refusedSlot.NextRequestAllowedAt);
                }

                StringContent content = new StringContent(requestJson, Encoding.UTF8, "application/json");

                using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, requestUrl)
                {
                    Content = content
                };

                request.Options.Set(WildberriesAuthHandler.ConnectionIdOptionKey, connectionId);

                using HttpResponseMessage response = await _httpClient.SendAsync(request, ct);

                ApiResponse<ProductCardsData> apiResponse = new ApiResponse<ProductCardsData>
                {
                    StatusCode = (int)response.StatusCode,
                    Timestamp = _timeProvider.GetUtcNow()
                };

                if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
                {
                    string responseBody = await response.Content.ReadAsStringAsync(ct);
                    _logger.LogWarning(
                        "Wildberries cards API returned {StatusCode}. Authentication failed. Response: {Response}",
                        (int)response.StatusCode,
                        TruncateForLog(responseBody));

                    apiResponse.IsSuccess = false;
                    apiResponse.ErrorMessage = $"Authentication failed: {response.StatusCode}";
                    apiResponse.Data = new ProductCardsData
                    {
                        Cards = new List<ChannelProductCard>(),
                        TotalCount = 0
                    };

                    return apiResponse;
                }

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    RateLimitSignal throttledSignal = WildberriesRateLimitHeaders.Parse(response);

                    DateTimeOffset retryAt = await _rateLimiter.ReportRateLimitedAsync(
                        scope, throttledSignal, ct);

                    _logger.LogWarning(
                        "Wildberries cards API rate limit exceeded (x-request-id {RequestId}, " +
                        "limit={Limit}, remaining={Remaining}, retry={RetrySeconds}s, reset={ResetSeconds}s). " +
                        "The next attempt is not due before {NextAttempt:O}.",
                        throttledSignal.RequestId,
                        throttledSignal.Limit,
                        throttledSignal.Remaining,
                        throttledSignal.RetrySeconds,
                        throttledSignal.ResetSeconds,
                        retryAt);

                    apiResponse.IsSuccess = false;
                    apiResponse.ErrorMessage = $"Rate limit exceeded. Retry after {retryAt:O}.";
                    apiResponse.Data = new ProductCardsData
                    {
                        Cards = new List<ChannelProductCard>(),
                        TotalCount = 0
                    };

                    return apiResponse;
                }

                if (!response.IsSuccessStatusCode)
                {
                    string responseBody = await response.Content.ReadAsStringAsync(ct);
                    _logger.LogWarning(
                        "Wildberries cards API returned {StatusCode}. Response: {Response}",
                        (int)response.StatusCode,
                        TruncateForLog(responseBody));

                    apiResponse.IsSuccess = false;
                    apiResponse.ErrorMessage = $"API error: {response.StatusCode}";
                    apiResponse.Data = new ProductCardsData
                    {
                        Cards = new List<ChannelProductCard>(),
                        TotalCount = 0
                    };

                    return apiResponse;
                }

                // Reported before the body is read: the counters describe this response, and a
                // body that fails to deserialize must not cost the limiter its only feedback on
                // the real allowance.
                await _rateLimiter.ReportSuccessAsync(
                    scope, WildberriesRateLimitHeaders.Parse(response), ct);

                WildberriesGetCardsResponse? cardsResponse =
                    await response.Content.ReadFromJsonAsync<WildberriesGetCardsResponse>(ct);

                if (cardsResponse == null)
                {
                    _logger.LogWarning("Wildberries cards API returned null response body.");

                    apiResponse.IsSuccess = false;
                    apiResponse.ErrorMessage = "Empty response from API.";
                    apiResponse.Data = new ProductCardsData
                    {
                        Cards = new List<ChannelProductCard>(),
                        TotalCount = 0
                    };

                    return apiResponse;
                }

                ProductCardsData data = WildberriesCardMapper.Map(cardsResponse, _logger);

                apiResponse.IsSuccess = true;
                apiResponse.Data = data;

                _logger.LogInformation(
                    "Retrieved {Count} product cards from Wildberries. Total: {Total}, HasNextPage: {HasNext}",
                    data.Cards.Count,
                    data.TotalCount,
                    data.NextCursor != null);

                return apiResponse;
            }
            catch (JsonException ex) when (attempt < DeserializationMaxAttempts)
            {
                // Retried in-place: a single malformed payload should not fail the whole import job.
                TimeSpan delay = DeserializationRetryDelay * attempt;

                _logger.LogWarning(
                    ex,
                    "Failed to parse Wildberries cards API response (attempt {Attempt} of {MaxAttempts}). Retrying in {DelayMs} ms.",
                    attempt,
                    DeserializationMaxAttempts,
                    delay.TotalMilliseconds);

                await Task.Delay(delay, ct);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP request failed when retrieving Wildberries product cards.");

                return new ApiResponse<ProductCardsData>
                {
                    IsSuccess = false,
                    ErrorMessage = $"Network error: {ex.Message}",
                    StatusCode = null,
                    Timestamp = _timeProvider.GetUtcNow(),
                    Data = new ProductCardsData
                    {
                        Cards = new List<ChannelProductCard>(),
                        TotalCount = 0
                    }
                };
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogWarning(ex, "Request timeout when retrieving Wildberries product cards.");

                return new ApiResponse<ProductCardsData>
                {
                    IsSuccess = false,
                    ErrorMessage = "Request timeout.",
                    StatusCode = null,
                    Timestamp = _timeProvider.GetUtcNow(),
                    Data = new ProductCardsData
                    {
                        Cards = new List<ChannelProductCard>(),
                        TotalCount = 0
                    }
                };
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Failed to parse Wildberries cards API response.");

                return new ApiResponse<ProductCardsData>
                {
                    IsSuccess = false,
                    ErrorMessage = "Invalid JSON response from API.",
                    StatusCode = null,
                    Timestamp = _timeProvider.GetUtcNow(),
                    Data = new ProductCardsData
                    {
                        Cards = new List<ChannelProductCard>(),
                        TotalCount = 0
                    }
                };
            }
        }
    }

    /// <inheritdoc cref="IOrdersSource.GetOrdersAsync"/>
    ///
    /// <remarks>
    /// The underlying endpoint (<c>/api/v1/supplier/orders</c>) accepts only <c>dateFrom</c> and
    /// <c>flag</c>: there is no server-side <c>dateTo</c> and no page size, so
    /// <see cref="OrdersRequest.DateTo"/> is applied client-side after the fetch.
    /// </remarks>
    public async Task<OrdersPage> GetOrdersAsync(
        Guid connectionId, OrdersRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_templateId == null)
        {
            throw new InvalidOperationException(
                "Template must be bound before calling API methods. Obtain the client from IChannelApiClientFactory.");
        }

        if (request.Schema != null)
        {
            throw new ArgumentException(
                "Wildberries does not support OrdersRequest.Schema; it is an Ozon-specific concept.",
                nameof(request));
        }

        DateTimeOffset dateFromMsk = request.DateFrom.ToOffset(MoscowOffset);
        DateTimeOffset dateToMsk = request.DateTo.ToOffset(MoscowOffset);

        // Rejected before the pacing slot is taken: a rejected request must not consume one of the
        // scarce calls, and a silently partial day is the defect this guard exists to prevent.
        if (request.Selection == OrdersSelectionMode.Placed && dateFromMsk.Date != dateToMsk.Date)
        {
            throw new ArgumentException(
                $"flag=1 selects orders of a single calendar day, but the requested window spans " +
                $"{dateFromMsk:yyyy-MM-dd} to {dateToMsk:yyyy-MM-dd} MSK. Request one day at a time.",
                nameof(request));
        }

        string baseUrl = await _endpointResolver.GetEndpointUrlAsync(
            _templateId.Value,
            ChannelEndpointType.Statistics,
            cancellationToken);

        Connection connection = await _connectionResolver.ResolveByIdAsync(connectionId, cancellationToken);

        RateLimitScope scope = BuildOrdersScope(connection.ApiKey);

        RateLimitAcquireResult acquireResult = await _rateLimiter.TryAcquireAsync(scope, cancellationToken);

        if (!acquireResult.Acquired)
        {
            // Not a failure and not an empty day: the caller keeps its cursor and returns later.
            _logger.LogInformation(
                "Wildberries orders request for connection {ConnectionId} was not issued: the rate " +
                "limiter has not released the next slot for method {Method} yet.",
                connectionId,
                scope.Method);

            return CreateThrottledPage(acquireResult.NextRequestAllowedAt);
        }

        string dateFromParameter = Uri.EscapeDataString(
            dateFromMsk.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture));

        string requestUrl = $"{baseUrl}/api/v1/supplier/orders?dateFrom={dateFromParameter}&flag={(int)request.Selection}";

        using HttpRequestMessage httpRequest = new HttpRequestMessage(HttpMethod.Get, requestUrl);
        httpRequest.Options.Set(WildberriesAuthHandler.ConnectionIdOptionKey, connectionId);

        using HttpResponseMessage response = await _httpClient.SendAsync(httpRequest, cancellationToken);

        RateLimitSignal rateLimitSignal = WildberriesRateLimitHeaders.Parse(response);

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            return await HandleThrottledResponseAsync(
                connection.ApiKey, connectionId, response, rateLimitSignal, cancellationToken);
        }

        if (!response.IsSuccessStatusCode)
        {
            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Wildberries orders API returned {StatusCode} (x-request-id {RequestId}). Response: {Response}",
                (int)response.StatusCode,
                rateLimitSignal.RequestId,
                TruncateForLog(responseBody));

            throw new HttpRequestException(
                $"Wildberries orders API returned {(int)response.StatusCode}.",
                null,
                response.StatusCode);
        }

        // Reported before the body is read: the counters describe this response, and a body that
        // fails to deserialize must not cost the limiter its only feedback on the real allowance.
        await _rateLimiter.ReportSuccessAsync(scope, rateLimitSignal, cancellationToken);

        List<WildberriesOrderDto>? rows =
            await response.Content.ReadFromJsonAsync<List<WildberriesOrderDto>>(cancellationToken);

        rows ??= new List<WildberriesOrderDto>();

        int maxRows = _options.MaxOrdersPerResponse > 0
            ? _options.MaxOrdersPerResponse
            : WildberriesImportOptions.DefaultMaxOrdersPerResponse;

        WildberriesOrderMappingResult mappingResult =
            WildberriesOrderMapper.Map(rows, request, maxRows, _logger);

        _logger.LogInformation(
            "Retrieved {Count} orders from Wildberries (flag={Flag}, dateFrom={DateFrom}, hasMore={HasMore}, coveredUntil={CoveredUntil}, rowsWithoutIdentity={RowsWithoutIdentity}).",
            mappingResult.Orders.Count,
            (int)request.Selection,
            dateFromMsk,
            mappingResult.HasMore,
            mappingResult.CoveredUntil,
            mappingResult.RowsWithoutIdentity);

        return new OrdersPage
        {
            Orders = mappingResult.Orders,
            HasMore = mappingResult.HasMore,
            CoveredUntil = mappingResult.CoveredUntil,
            RowsWithoutIdentity = mappingResult.RowsWithoutIdentity
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
            RowsWithoutIdentity = 0
        };
    }

    private ApiResponse<ProductCardsData> CreateDeferredCardsResponse(DateTimeOffset nextAllowedAt)
    {
        _logger.LogInformation(
            "Wildberries cards request was not issued: the rate limiter has not released the " +
            "next slot for method {Method} until {NextAllowed:O}.",
            WildberriesApiMethodRegistry.ContentCardsList,
            nextAllowedAt);

        return new ApiResponse<ProductCardsData>
        {
            IsSuccess = false,
            ErrorMessage = $"Rate limit: slot not available until {nextAllowedAt:O}.",
            StatusCode = 429,
            Timestamp = _timeProvider.GetUtcNow(),
            Data = new ProductCardsData
            {
                Cards = new List<ChannelProductCard>(),
                TotalCount = 0
            }
        };
    }

    private RateLimitScope BuildOrdersScope(string apiKey)
    {
        return new RateLimitScope(
            ChannelCode, apiKey, WildberriesApiMethodRegistry.SupplierOrders);
    }

    private async Task<OrdersPage> HandleThrottledResponseAsync(
        string apiKey,
        Guid connectionId,
        HttpResponseMessage response,
        RateLimitSignal signal,
        CancellationToken cancellationToken)
    {
        string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        DateTimeOffset retryAt = await _rateLimiter.ReportRateLimitedAsync(
            BuildOrdersScope(apiKey),
            signal,
            cancellationToken);

        _logger.LogWarning(
            "Wildberries orders API throttled the request (429): connection {ConnectionId}, method " +
            "{Method}, key fingerprint {KeyFingerprint}, x-request-id {RequestId}, " +
            "x-ratelimit-limit {Limit}, x-ratelimit-remaining {Remaining}, " +
            "x-ratelimit-retry {RetrySeconds}s, x-ratelimit-reset {ResetSeconds}s. " +
            "Retry is taken from x-ratelimit-retry, not from x-ratelimit-reset. Response: {Response}",
            connectionId,
            WildberriesApiMethodRegistry.SupplierOrders,
            ApiKeyFingerprint.Compute(apiKey),
            signal.RequestId,
            signal.Limit,
            signal.Remaining,
            signal.RetrySeconds,
            signal.ResetSeconds,
            TruncateForLog(responseBody));

        return CreateThrottledPage(retryAt);
    }

    private static string TruncateForLog(string responseBody)
    {
        if (string.IsNullOrEmpty(responseBody) || responseBody.Length <= ResponseBodyLogLimit)
        {
            return responseBody;
        }

        return responseBody.Substring(0, ResponseBodyLogLimit);
    }

    private sealed class WildberriesSellerInfoResponse
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("tin")]
        public string? Tin { get; init; }

        [JsonPropertyName("trademark")]
        public string? Trademark { get; init; }
    }
}
