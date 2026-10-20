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
using Delobytes.App.Backend.Integrations.Application.Services.MarketplaceClients;
using Delobytes.App.Backend.Integrations.Contracts.Models;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Delobytes.App.Backend.Integrations.Infrastructure.ApiClients;

/// <summary>
/// Wildberries marketplace API client implementation.
/// </summary>
public class WildberriesApiClient : IChannelApiClient, IProductsSource, IOrdersSource
{
    /// <summary>
    /// Number of attempts made when a response body cannot be deserialized. Kept small because
    /// a payload that trips the deserializer is usually deterministic within one import run.
    /// </summary>
    private const int DeserializationMaxAttempts = 3;

    /// <summary>
    /// Wildberries settles in rubles; the orders endpoint reports no currency field of its own.
    /// </summary>
    private const string WildberriesCurrency = "RUB";

    /// <summary>
    /// Longest <c>gNumber</c> or <c>srid</c>/sticker identity this mapper accepts. Matches the
    /// <c>HasMaxLength(100)</c> on both <c>Order.ExternalOrderId</c> and <c>OrderLine.LineKey</c>
    /// </summary>
    private const int MaxIdentityLength = 100;

    /// <summary>
    /// Maximum number of response body characters written to logs.
    /// </summary>
    private const int ResponseBodyLogLimit = 500;

    /// <summary>
    /// Namespace prefix for order identities derived from the sticker. Wildberries reports
    /// <c>srid</c> as an opaque token, so a prefixed sticker cannot be mistaken for one and,
    /// unlike a bare sticker, cannot be confused with any identifier a future payload adds.
    /// </summary>
    private const string StickerIdentityPrefix = "wb-stk:";

    /// <summary>
    /// Base delay between deserialization attempts; grows linearly per attempt.
    /// </summary>
    private static readonly TimeSpan DefaultDeserializationRetryDelay = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Wildberries has operated on a fixed UTC+3 offset (no DST) since 2014; the orders API
    /// takes dates in this local time and does not accept a time zone designator.
    /// </summary>
    private static readonly TimeSpan MoscowOffset = TimeSpan.FromHours(3);

    private readonly HttpClient _httpClient;
    private readonly IEndpointResolver _endpointResolver;
    private readonly IConnectionResolver _connectionResolver;
    private readonly IWildberriesRateLimiter _rateLimiter;
    private readonly ILogger<WildberriesApiClient> _logger;
    private readonly WildberriesImportOptions _options;

    private Guid? _templateId;

    private string? _channelCode;

    /// <summary>
    /// Initializes a new instance of the <see cref="WildberriesApiClient"/> class.
    /// </summary>
    public WildberriesApiClient(
        HttpClient httpClient,
        IEndpointResolver endpointResolver,
        IConnectionResolver connectionResolver,
        IWildberriesRateLimiter rateLimiter,
        IOptions<WildberriesImportOptions> options,
        ILogger<WildberriesApiClient> logger)
    {
        _httpClient = httpClient;
        _endpointResolver = endpointResolver;
        _connectionResolver = connectionResolver;
        _rateLimiter = rateLimiter;
        _options = options.Value;
        _logger = logger;
    }

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
    /// Binds this client instance to a system channel template.
    /// Called by <see cref="ChannelApiClientFactory"/> before the client is returned to a caller.
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

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Wildberries seller-info returned {StatusCode}.", (int)response.StatusCode);
                return null;
            }

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

        for (int attempt = 1; ; attempt++)
        {
            try
            {
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
                    Timestamp = DateTimeOffset.UtcNow
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
                        Cards = new List<WildberriesCardSnapshot>(),
                        TotalCount = 0
                    };

                    return apiResponse;
                }

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    string retryAfter = response.Headers.RetryAfter?.Delta?.TotalSeconds.ToString() ?? "unknown";
                    _logger.LogWarning(
                        "Wildberries cards API rate limit exceeded. Retry after {RetryAfter} seconds.",
                        retryAfter);

                    apiResponse.IsSuccess = false;
                    apiResponse.ErrorMessage = $"Rate limit exceeded. Retry after {retryAfter} seconds.";
                    apiResponse.Data = new ProductCardsData
                    {
                        Cards = new List<WildberriesCardSnapshot>(),
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
                        Cards = new List<WildberriesCardSnapshot>(),
                        TotalCount = 0
                    };

                    return apiResponse;
                }

                WildberriesGetCardsResponse? cardsResponse =
                    await response.Content.ReadFromJsonAsync<WildberriesGetCardsResponse>(ct);

                if (cardsResponse == null)
                {
                    _logger.LogWarning("Wildberries cards API returned null response body.");

                    apiResponse.IsSuccess = false;
                    apiResponse.ErrorMessage = "Empty response from API.";
                    apiResponse.Data = new ProductCardsData
                    {
                        Cards = new List<WildberriesCardSnapshot>(),
                        TotalCount = 0
                    };

                    return apiResponse;
                }

                List<WildberriesCardSnapshot> snapshots = cardsResponse.Cards
                    .Select(MapToSnapshot)
                    .ToList();

                ProductCardsCursor? nextCursor = null;
                if (cardsResponse.Cursor?.UpdatedAt != null && cardsResponse.Cursor?.NmId != null)
                {
                    nextCursor = new ProductCardsCursor
                    {
                        UpdatedAt = cardsResponse.Cursor.UpdatedAt,
                        ProductId = cardsResponse.Cursor.NmId.Value
                    };
                }

                apiResponse.IsSuccess = true;
                apiResponse.Data = new ProductCardsData
                {
                    Cards = snapshots,
                    NextCursor = nextCursor,
                    TotalCount = cardsResponse.Cursor?.Total ?? snapshots.Count
                };

                _logger.LogInformation(
                    "Retrieved {Count} product cards from Wildberries. Total: {Total}, HasNextPage: {HasNext}",
                    snapshots.Count,
                    apiResponse.Data.TotalCount,
                    nextCursor != null);

                return apiResponse;
            }
            catch (JsonException ex) when (attempt < DeserializationMaxAttempts)
            {
                // Retried in-place: a single malformed payload should not fail the whole import
                // job. The HTTP request is rebuilt each attempt because HttpRequestMessage
                // cannot be sent twice.
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
                    Timestamp = DateTimeOffset.UtcNow,
                    Data = new ProductCardsData
                    {
                        Cards = new List<WildberriesCardSnapshot>(),
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
                    Timestamp = DateTimeOffset.UtcNow,
                    Data = new ProductCardsData
                    {
                        Cards = new List<WildberriesCardSnapshot>(),
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
                    Timestamp = DateTimeOffset.UtcNow,
                    Data = new ProductCardsData
                    {
                        Cards = new List<WildberriesCardSnapshot>(),
                        TotalCount = 0
                    }
                };
            }
        }
    }

    /// <inheritdoc cref="IProductsSource.GetProductCardsAsync"/>
    ///
    /// <remarks>
    /// Thin adapter over the cursor/limit overload above: the capability interface carries no
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
            Cards = response.Data?.Cards.ToList() ?? new List<WildberriesCardSnapshot>(),
            NextCursor = response.Data?.NextCursor,
            TotalCount = response.Data?.TotalCount ?? 0
        };
    }

    /// <inheritdoc cref="IOrdersSource.GetOrdersAsync"/>
    ///
    /// <remarks>
    /// The underlying endpoint (<c>/api/v1/supplier/orders</c>) accepts only <c>dateFrom</c> and
    /// <c>flag</c>: there is no server-side <c>dateTo</c> and no page size, so
    /// <see cref="OrdersRequest.DateTo"/> is applied client-side after the fetch.
    /// <see cref="OrdersRequest.Limit"/> is advisory only and is deliberately not applied here -
    /// the endpoint cannot honour it, and truncating locally would drop orders the caller asked
    /// for, which is exactly the data loss this path must not reintroduce.
    /// The endpoint does cap a single response by <see cref="WildberriesImportOptions.MaxOrdersPerResponse"/>
    /// rows, so <see cref="OrdersPage.HasMore"/> reports whether the response was a partial subset
    /// of the requested window and the caller has to narrow it and fetch again.
    /// <para>
    /// Coverage: <c>!hasMore</c> is reported as <see cref="OrdersPage.CoveredUntil"/> equal to
    /// <see cref="OrdersRequest.DateTo"/> <b>only</b> because a <c>flag=1</c> request spans at most
    /// one calendar day here. <c>flag=1</c> is a single-day selector, not a range filter: a request
    /// whose window spans several days returns the orders of exactly one of them and would still
    /// look complete, so a full response over a multi-day window would claim coverage it never
    /// fetched. Such a request is rejected with <see cref="ArgumentException"/> below rather than
    /// answered with a misleading page.
    /// </para>
    /// <para>
    /// Pacing: every call passes the adaptive limiter first. A refused slot or a <c>429</c> both
    /// surface as <see cref="OrdersPage.Throttled"/> and never as an exception, because a throttle
    /// is a scheduling fact the caller acts on, not a failure of the run.
    /// </para>
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

        DateTimeOffset dateFromMsk = request.DateFrom.ToOffset(MoscowOffset);
        DateTimeOffset dateToMsk = request.DateTo.ToOffset(MoscowOffset);

        // Rejected before the pacing slot is taken: a rejected request must not consume one of the
        // scarce calls, and a silently partial day is the defect this guard exists to prevent.
        if (request.Flag == 1 && dateFromMsk.Date != dateToMsk.Date)
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

        bool slotAcquired = await _rateLimiter.TryAcquireAsync(
            connection.ApiKey,
            WildberriesApiMethod.SupplierOrders,
            cancellationToken);

        if (!slotAcquired)
        {
            // Not a failure and not an empty day: the caller keeps its cursor and returns later.
            _logger.LogInformation(
                "Wildberries orders request for connection {ConnectionId} was not issued: the rate " +
                "limiter has not released the next slot for method {Method} yet.",
                connectionId,
                WildberriesApiMethod.SupplierOrders);

            return CreateThrottledPage();
        }

        string dateFromParameter = Uri.EscapeDataString(
            dateFromMsk.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture));

        string requestUrl = $"{baseUrl}/api/v1/supplier/orders?dateFrom={dateFromParameter}&flag={request.Flag}";

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
        await _rateLimiter.ReportSuccessAsync(
            connection.ApiKey,
            WildberriesApiMethod.SupplierOrders,
            rateLimitSignal,
            cancellationToken);

        List<WildberriesOrderDto>? orders =
            await response.Content.ReadFromJsonAsync<List<WildberriesOrderDto>>(cancellationToken);

        orders ??= new List<WildberriesOrderDto>();

        List<ChannelOrderSnapshot> snapshots = new List<ChannelOrderSnapshot>();
        Dictionary<string, List<WildberriesOrderDto>> rowsByOrderKey = new Dictionary<string, List<WildberriesOrderDto>>(StringComparer.Ordinal);

        // Keeps the emitted snapshot order tied to the first row of each cart rather than to
        // Dictionary enumeration, so the batch a cart lands in does not move between runs.
        List<string> orderKeysInArrivalOrder = new List<string>();

        int rowsWithoutIdentity = 0;

        // Bounds how far into the window the returned rows reach. Recorded before the window filter
        // below, because a row past dateTo is exactly what consumed the response cap, and so is what
        // limits what this response can claim to have covered.
        DateTimeOffset? coveredUntil = null;
        DateTimeOffset? previousSelectionInstant = null;

        // A capped response is a prefix of the rows, so the largest returned instant bounds what was
        // served only while the rows arrive in ascending order of the instant they were selected by.
        // The endpoint orders by lastChangeDate while flag=1 selects by order date, so the two axes
        // coincide for a delta run and may diverge for a backfill; taking the maximum there would
        // claim coverage of a region whose rows were cut off, and the caller would resume past them.
        bool coversContiguousPrefix = true;

        foreach (WildberriesOrderDto order in orders)
        {
            DateTimeOffset? selectionInstant = GetSelectionInstant(order, request.Flag);

            if (selectionInstant != null)
            {
                if (previousSelectionInstant != null && selectionInstant.Value < previousSelectionInstant.Value)
                {
                    coversContiguousPrefix = false;
                }

                previousSelectionInstant = selectionInstant;

                if (coveredUntil == null || selectionInstant.Value > coveredUntil.Value)
                {
                    coveredUntil = selectionInstant.Value;
                }
            }

            if (!IsWithinRequestedWindow(order, request.Flag, dateToMsk))
            {
                continue;
            }

            string? lineKey = GetLineKey(order);

            if (lineKey == null)
            {
                // Never fabricate a key here: any composite over nmId, techSize, gNumber and date
                // is not proven unique per order (one cart may hold two units of the same article
                // and size, and Wildberries reports date to the second), so resolving the
                // collision would merge two real orders into one and report the loss as a
                // successful import. The row is dropped and counted instead.
                rowsWithoutIdentity++;

                _logger.LogError(
                    "Wildberries order row without usable identity skipped: NmId={NmId}, GNumber={GNumber}, Date={Date}, Sticker={Sticker}.",
                    order.NmId,
                    order.GNumber,
                    order.Date,
                    order.Sticker);

                continue;
            }

            EnsureIdentityLength(lineKey, nameof(lineKey));

            string? orderKey = GetOrderKey(order);

            if (orderKey == null)
            {
                // Distinct from the missing line identity above: this row does have a line identity,
                // it lacks the order identity that would place it in a cart. Conflating the two is
                // what let a cart be split into one order per unit.
                rowsWithoutIdentity++;

                _logger.LogError(
                    "Wildberries order row without a gNumber skipped: NmId={NmId}, Srid={Srid}, Date={Date}, Sticker={Sticker}.",
                    order.NmId,
                    order.Srid,
                    order.Date,
                    order.Sticker);

                continue;
            }

            EnsureIdentityLength(orderKey, nameof(orderKey));

            if (!rowsByOrderKey.TryGetValue(orderKey, out List<WildberriesOrderDto>? rows))
            {
                rows = new List<WildberriesOrderDto>();
                rowsByOrderKey.Add(orderKey, rows);
                orderKeysInArrivalOrder.Add(orderKey);
            }
            else if (ContainsLineKey(rows, lineKey))
            {
                // Unlike a repeated order key, a repeated line key inside one order cannot describe
                // the response: one srid is one ordered unit, so two rows claiming it are two
                // versions of that unit rather than two units. Keeping either would silently decide
                // which reported row the import believes.
                throw new InvalidOperationException(
                    $"Wildberries returned line key '{lineKey}' more than once within order '{orderKey}' in one response.");
            }

            rows.Add(order);
        }

        foreach (string orderKey in orderKeysInArrivalOrder)
        {
            snapshots.Add(MapToOrderSnapshot(orderKey, rowsByOrderKey[orderKey]));
        }

        int maxRows = _options.MaxOrdersPerResponse > 0
            ? _options.MaxOrdersPerResponse
            : WildberriesImportOptions.DefaultMaxOrdersPerResponse;

        bool hasMore = orders.Count >= maxRows;

        _logger.LogInformation(
            "Retrieved {Count} orders from Wildberries (flag={Flag}, dateFrom={DateFrom}, hasMore={HasMore}, coveredUntil={CoveredUntil}, rowsWithoutIdentity={RowsWithoutIdentity}).",
            snapshots.Count,
            request.Flag,
            dateFromMsk,
            hasMore,
            coveredUntil,
            rowsWithoutIdentity);

        return new OrdersPage
        {
            Orders = snapshots,
            HasMore = hasMore,

            // A response under the cap came back whole, so it covers the window end that was asked
            // for. A capped one covers only as far as its last returned selection instant, and only
            // when its rows arrived ordered by that instant.
            CoveredUntil = !hasMore
                ? request.DateTo
                : coversContiguousPrefix ? coveredUntil : null,
            RowsWithoutIdentity = rowsWithoutIdentity
        };
    }

    /// <summary>
    /// Builds the page returned when no request was issued or the API refused it.
    /// </summary>
    private static OrdersPage CreateThrottledPage()
    {
        return new OrdersPage
        {
            Orders = new List<ChannelOrderSnapshot>(),
            HasMore = false,
            CoveredUntil = null,
            Throttled = true,
            RowsWithoutIdentity = 0
        };
    }

    /// <summary>
    /// Records a <c>429</c> with the rate limiter and returns the throttled page.
    /// </summary>
    private async Task<OrdersPage> HandleThrottledResponseAsync(
        string apiKey,
        Guid connectionId,
        HttpResponseMessage response,
        RateLimitSignal signal,
        CancellationToken cancellationToken)
    {
        string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        await _rateLimiter.ReportRateLimitedAsync(
            apiKey,
            WildberriesApiMethod.SupplierOrders,
            signal,
            cancellationToken);

        _logger.LogWarning(
            "Wildberries orders API throttled the request (429): connection {ConnectionId}, method " +
            "{Method}, key fingerprint {KeyFingerprint}, x-request-id {RequestId}, " +
            "x-ratelimit-limit {Limit}, x-ratelimit-remaining {Remaining}, " +
            "x-ratelimit-retry {RetrySeconds}s, x-ratelimit-reset {ResetSeconds}s. " +
            "Retry is taken from x-ratelimit-retry, not from x-ratelimit-reset. Response: {Response}",
            connectionId,
            WildberriesApiMethod.SupplierOrders,
            WildberriesKeyFingerprint.Compute(apiKey),
            signal.RequestId,
            signal.Limit,
            signal.Remaining,
            signal.RetrySeconds,
            signal.ResetSeconds,
            TruncateForLog(responseBody));

        return CreateThrottledPage();
    }

    /// <summary>
    /// Resolves the identity of one ordered unit. <c>srid</c> is the documented per-order id and is
    /// returned unmodified; <c>sticker</c> is the only other field the payload guarantees one of
    /// per ordered unit, and is returned with a prefix so it stays recognisable as a derived
    /// identity. Everything else is mutable — <c>lastChangeDate</c>, <c>isCancel</c>,
    /// <c>cancelDate</c> and the price fields all change after the fact — and keying on any of
    /// them would re-key existing rows on the next import.
    /// </summary>
    /// <remarks>
    /// The result is a line identity, not an order identity: it is unique within the owning order,
    /// and two carts may carry the same <c>srid</c>.
    /// </remarks>
    private static string? GetLineKey(WildberriesOrderDto dto)
    {
        if (!string.IsNullOrWhiteSpace(dto.Srid))
        {
            return dto.Srid;
        }

        if (!string.IsNullOrWhiteSpace(dto.Sticker))
        {
            return StickerIdentityPrefix + dto.Sticker;
        }

        return null;
    }

    /// <summary>
    /// Resolves the identity of one cart. <c>gNumber</c> is the only field that groups the units of
    /// one cart and the only one that is immutable, so it is the sole source of the order key; a row
    /// that does not report one has no placeable order identity and yields null.
    /// </summary>
    /// <remarks>
    /// There is deliberately no fallback to the line identity. A cart reported with <c>gNumber</c> in
    /// one run and without it in another would then be persisted twice, under two different keys,
    /// and both rows would satisfy every constraint — the duplicate would be invisible to the
    /// database and findable only by eye. A duplicate is worse than a visible failure, so the row is
    /// counted and the page fails instead. The line identity may still be derived from a sticker: a
    /// line is addressed inside an order, and that order is stable.
    /// <para>
    /// <c>Order</c> is unique on (TenantId, ChannelId, ExternalOrderId); that is sufficient because
    /// a tenant may bind at most one active connection per channel, enforced by
    /// <c>CreateConnectionCommandHandler</c> through <c>HasActiveConnectionForChannelAsync</c>.
    /// Two cabinets on one channel cannot coexist, so two <c>gNumber</c> sequences cannot meet on
    /// this index.
    /// </para>
    /// </remarks>
    private static string? GetOrderKey(WildberriesOrderDto dto)
    {
        return string.IsNullOrWhiteSpace(dto.GNumber) ? null : dto.GNumber;
    }

    private static void EnsureIdentityLength(string identity, string name)
    {
        if (identity.Length > MaxIdentityLength)
        {
            throw new InvalidOperationException(
                $"Wildberries computed {name} '{identity}' exceeds the maximum persistable length of {MaxIdentityLength} characters.");
        }
    }

    private static bool ContainsLineKey(List<WildberriesOrderDto> rows, string lineKey)
    {
        foreach (WildberriesOrderDto row in rows)
        {
            if (string.Equals(GetLineKey(row), lineKey, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string TruncateForLog(string responseBody)
    {
        if (string.IsNullOrEmpty(responseBody) || responseBody.Length <= ResponseBodyLogLimit)
        {
            return responseBody;
        }

        return responseBody.Substring(0, ResponseBodyLogLimit);
    }

    /// <summary>
    /// Resolves the instant the endpoint selects on: flag=0 (delta) selects by lastChangeDate,
    /// flag=1 (backfill) by the order date. This is the axis a capped response is bounded by, so it
    /// is the only correct source for how far the response reached. Null when the row carries no
    /// parseable instant on that axis.
    /// </summary>
    private static DateTimeOffset? GetSelectionInstant(WildberriesOrderDto dto, int flag)
    {
        return flag == 0
            ? ParseMoscowTimestamp(dto.LastChangeDate) ?? ParseMoscowTimestamp(dto.Date)
            : ParseMoscowTimestamp(dto.Date);
    }

    /// <summary>
    /// Filters out rows outside the requested window. flag=0 (delta) windows by lastChangeDate;
    /// flag=1 (backfill) windows by the order date itself, matching what each flag selects on
    /// the Wildberries side.
    /// </summary>
    private static bool IsWithinRequestedWindow(WildberriesOrderDto dto, int flag, DateTimeOffset dateToMsk)
    {
        DateTimeOffset? reference = GetSelectionInstant(dto, flag);

        return reference is null || reference.Value <= dateToMsk;
    }

    /// <summary>
    /// Wildberries reports timestamps as local (MSK) time without an offset designator; parsing
    /// them as UTC or local-to-this-process would silently shift them by an arbitrary amount.
    /// </summary>
    private static DateTimeOffset? ParseMoscowTimestamp(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (!DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime local))
        {
            return null;
        }

        return new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), MoscowOffset)
            .ToUniversalTime();
    }

    /// <summary>
    /// Maps the rows of one cart to a snapshot. <paramref name="orderKey"/> is resolved by the
    /// caller through <see cref="GetOrderKey"/>; the rows are the units of that one cart and each
    /// becomes a line.
    /// </summary>
    /// <remarks>
    /// The header fields are reduced from the rows because the snapshot describes the cart, and the
    /// cart has no header of its own in the payload. Fields Wildberries reports per unit and that
    /// have no cart-level meaning take the first row's value rather than an invented merge rule.
    /// </remarks>
    private ChannelOrderSnapshot MapToOrderSnapshot(string orderKey, List<WildberriesOrderDto> rows)
    {
        WildberriesOrderDto first = rows[0];

        List<ChannelOrderLineSnapshot> lines = new List<ChannelOrderLineSnapshot>(rows.Count);
        List<DateTimeOffset> rowOrderDates = new List<DateTimeOffset>(rows.Count);

        foreach (WildberriesOrderDto row in rows)
        {
            string lineKey = GetLineKey(row)
                ?? throw new InvalidOperationException(
                    $"Wildberries order '{orderKey}' contains a row without a usable line identity.");

            lines.Add(MapToOrderLine(row, lineKey));

            DateTimeOffset? rowOrderDate = ParseMoscowTimestamp(row.Date);

            if (rowOrderDate != null)
            {
                rowOrderDates.Add(rowOrderDate.Value);
            }
        }

        if (rowOrderDates.Count == 0)
        {
            // Substituting the wall clock here would make every refetch of an unchanged cart differ,
            // and the difference would surface upstream as an update on every run.
            throw new InvalidOperationException(
                $"Wildberries order '{orderKey}' reports no row with a parseable date; " +
                "the cart carries no placement instant and one cannot be invented.");
        }

        // Every cart-level field below is reduced from the lines and from the rows' dates by this
        // one helper; nothing else in the codebase may compute them.
        ChannelOrderHeaderFacts header = ChannelOrderHeaderFacts.Reduce(lines, rowOrderDates);

        bool? isSellerWarehouse = first.WarehouseType?.Contains("продавца", StringComparison.OrdinalIgnoreCase);

        return new ChannelOrderSnapshot
        {
            ExternalOrderId = orderKey,

            // gNumber is the buyer-facing order number, so it is both the identity and the number;
            // reporting it twice is what the contract asks for.
            ExternalOrderNumber = orderKey,
            OrderDate = header.OrderDate,
            StatusChangedAt = header.StatusChangedAt,
            ExternalStatus = header.ExternalStatus,

            // Reported per unit with no cart-level counterpart, so the first row stands in for the
            // cart instead of a merge rule the payload does not define.
            ExternalSubstatus = first.OrderType,
            CancelReason = header.CancelReason,
            Currency = WildberriesCurrency,
            DeliveryRegion = first.RegionName,
            WarehouseName = first.WarehouseName,
            IsSellerWarehouse = isSellerWarehouse,
            ChannelReportedTotal = header.ChannelReportedTotal,
            Lines = lines
        };
    }

    /// <summary>
    /// Maps one row to one line. <paramref name="lineKey"/> is resolved by the caller through
    /// <see cref="GetLineKey"/> and used verbatim so the line keeps the identity it was grouped by.
    /// </summary>
    private static ChannelOrderLineSnapshot MapToOrderLine(WildberriesOrderDto dto, string lineKey)
    {
        return new ChannelOrderLineSnapshot
        {
            LineKey = lineKey,
            ExternalLineId = dto.Srid,
            ExternalProductId = dto.NmId.ToString(CultureInfo.InvariantCulture),
            OfferId = dto.SupplierArticle,
            ExternalSizeId = dto.TechSize,
            ProductName = dto.Subject,
            Quantity = 1,
            UnitPrice = dto.PriceWithDisc,
            BuyerUnitPrice = dto.FinishedPrice,
            UnitPriceBeforeDiscount = dto.TotalPrice,
            Currency = WildberriesCurrency,
            IsCancelled = dto.IsCancel,
            StatusChangedAt = ParseMoscowTimestamp(dto.LastChangeDate),
            ChannelReportedTotal = dto.TotalPrice
        };
    }

    private WildberriesCardSnapshot MapToSnapshot(WildberriesCardDto dto)
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
            _logger.LogWarning(ex, "Failed to serialize channel-specific data for nmID {NmId}.", dto.NmId);
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

    private sealed class WildberriesSellerInfoResponse
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("tin")]
        public string? Tin { get; init; }

        [JsonPropertyName("trademark")]
        public string? Trademark { get; init; }
    }

    /// <summary>One row of the <c>/api/v1/supplier/orders</c> response: one ordered unit.</summary>
    private sealed class WildberriesOrderDto
    {
        [JsonPropertyName("date")]
        public string? Date { get; init; }

        [JsonPropertyName("lastChangeDate")]
        public string? LastChangeDate { get; init; }

        [JsonPropertyName("warehouseName")]
        public string? WarehouseName { get; init; }

        [JsonPropertyName("warehouseType")]
        public string? WarehouseType { get; init; }

        [JsonPropertyName("regionName")]
        public string? RegionName { get; init; }

        [JsonPropertyName("supplierArticle")]
        public string? SupplierArticle { get; init; }

        [JsonPropertyName("nmId")]
        public long NmId { get; init; }

        [JsonPropertyName("subject")]
        public string? Subject { get; init; }

        [JsonPropertyName("techSize")]
        public string? TechSize { get; init; }

        [JsonPropertyName("totalPrice")]
        public decimal? TotalPrice { get; init; }

        [JsonPropertyName("priceWithDisc")]
        public decimal? PriceWithDisc { get; init; }

        [JsonPropertyName("finishedPrice")]
        public decimal? FinishedPrice { get; init; }

        [JsonPropertyName("isCancel")]
        public bool IsCancel { get; init; }

        [JsonPropertyName("orderType")]
        public string? OrderType { get; init; }

        [JsonPropertyName("gNumber")]
        public string? GNumber { get; init; }

        [JsonPropertyName("srid")]
        public string? Srid { get; init; }

        /// <summary>
        /// Gets the identifier of the item sticker: one per ordered unit, which makes it the only
        /// field besides <see cref="Srid"/> that Wildberries guarantees unique per order.
        /// </summary>
        [JsonPropertyName("sticker")]
        public string? Sticker { get; init; }
    }
}
