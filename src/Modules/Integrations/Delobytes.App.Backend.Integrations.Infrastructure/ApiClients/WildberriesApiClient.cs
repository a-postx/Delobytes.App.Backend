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
        IOptions<WildberriesImportOptions> options,
        ILogger<WildberriesApiClient> logger)
    {
        _httpClient = httpClient;
        _endpointResolver = endpointResolver;
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
    public string ChannelCode =>
        _channelCode ?? throw new InvalidOperationException(
            "Template must be bound before reading ChannelCode. Obtain the client from IChannelApiClientFactory.");

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

        string baseUrl = await _endpointResolver.GetEndpointUrlAsync(
            _templateId.Value,
            ChannelEndpointType.Statistics,
            cancellationToken);

        DateTimeOffset dateFromMsk = request.DateFrom.ToOffset(MoscowOffset);
        DateTimeOffset dateToMsk = request.DateTo.ToOffset(MoscowOffset);

        string dateFromParameter = Uri.EscapeDataString(
            dateFromMsk.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture));

        string requestUrl = $"{baseUrl}/api/v1/supplier/orders?dateFrom={dateFromParameter}&flag={request.Flag}";

        using HttpRequestMessage httpRequest = new HttpRequestMessage(HttpMethod.Get, requestUrl);
        httpRequest.Options.Set(WildberriesAuthHandler.ConnectionIdOptionKey, connectionId);

        using HttpResponseMessage response = await _httpClient.SendAsync(httpRequest, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Wildberries orders API returned {StatusCode}. Response: {Response}",
                (int)response.StatusCode,
                TruncateForLog(responseBody));

            throw new HttpRequestException(
                $"Wildberries orders API returned {(int)response.StatusCode}.",
                null,
                response.StatusCode);
        }

        List<WildberriesOrderDto>? orders =
            await response.Content.ReadFromJsonAsync<List<WildberriesOrderDto>>(cancellationToken);

        orders ??= new List<WildberriesOrderDto>();

        List<ChannelOrderSnapshot> snapshots = new List<ChannelOrderSnapshot>();
        Dictionary<string, WildberriesOrderDto> rowsByExternalOrderId = new Dictionary<string, WildberriesOrderDto>(StringComparer.Ordinal);

        int rowsWithoutIdentity = 0;

        foreach (WildberriesOrderDto order in orders)
        {
            if (!IsWithinRequestedWindow(order, request.Flag, dateToMsk))
            {
                continue;
            }

            string? externalOrderId = GetExternalOrderId(order);

            if (externalOrderId == null)
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

            if (rowsByExternalOrderId.ContainsKey(externalOrderId))
            {
                // One response naming the same order twice is a data-quality failure, not a
                // duplicate to resolve: within a single response there is no window overlap that
                // could explain a legitimate repeat, and picking either row would silently decide
                // which of two reported orders the import keeps.
                throw new InvalidOperationException(
                    $"Wildberries returned external order id '{externalOrderId}' more than once in one response " +
                    "(NmId and gNumber lines do not identify a single order, so the response cannot be deduplicated).");
            }

            rowsByExternalOrderId.Add(externalOrderId, order);
            snapshots.Add(MapToOrderSnapshot(order, externalOrderId));
        }

        int maxRows = _options.MaxOrdersPerResponse > 0
            ? _options.MaxOrdersPerResponse
            : WildberriesImportOptions.DefaultMaxOrdersPerResponse;

        bool hasMore = orders.Count >= maxRows;

        _logger.LogInformation(
            "Retrieved {Count} orders from Wildberries (flag={Flag}, dateFrom={DateFrom}, hasMore={HasMore}, rowsWithoutIdentity={RowsWithoutIdentity}).",
            snapshots.Count,
            request.Flag,
            dateFromMsk,
            hasMore,
            rowsWithoutIdentity);

        return new OrdersPage
        {
            Orders = snapshots,
            HasMore = hasMore,
            RowsWithoutIdentity = rowsWithoutIdentity
        };
    }

    /// <summary>
    /// Resolves the identity of one order row. <c>srid</c> is the documented per-order id and is
    /// returned unmodified; <c>sticker</c> is the only other field the payload guarantees one of
    /// per ordered unit, and is returned with a prefix so it stays recognisable as a derived
    /// identity. Everything else is mutable — <c>lastChangeDate</c>, <c>isCancel</c>,
    /// <c>cancelDate</c> and the price fields all change after the fact — and keying on any of
    /// them would re-key existing rows on the next import. The connection is deliberately not
    /// part of the identity either: reconnecting a cabinet would then duplicate its whole history.
    /// </summary>
    /// <remarks>
    /// UNRESOLVED: <c>Order</c> is unique on (TenantId, ChannelId, ExternalOrderId), so this chain
    /// is only a per-order identity while a tenant binds at most one cabinet to a given ChannelId.
    /// Nothing enforces that today — Connection carries no such constraint — and two cabinets on
    /// one channel whose <c>srid</c> values happen to coincide would upsert onto each other's rows.
    /// Deciding between enforcing the invariant and moving the uniqueness scope onto the connection
    /// is a product call, not a mapping one; until it is made, the mapping cannot defend itself.
    /// </remarks>
    private static string? GetExternalOrderId(WildberriesOrderDto dto)
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

    private static string TruncateForLog(string responseBody)
    {
        if (string.IsNullOrEmpty(responseBody) || responseBody.Length <= ResponseBodyLogLimit)
        {
            return responseBody;
        }

        return responseBody.Substring(0, ResponseBodyLogLimit);
    }

    /// <summary>
    /// Filters out rows outside the requested window. flag=0 (delta) windows by lastChangeDate;
    /// flag=1 (backfill) windows by the order date itself, matching what each flag selects on
    /// the Wildberries side.
    /// </summary>
    private static bool IsWithinRequestedWindow(WildberriesOrderDto dto, int flag, DateTimeOffset dateToMsk)
    {
        DateTimeOffset? reference = flag == 0
            ? ParseMoscowTimestamp(dto.LastChangeDate) ?? ParseMoscowTimestamp(dto.Date)
            : ParseMoscowTimestamp(dto.Date);

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
    /// Maps one row to a snapshot. <paramref name="externalOrderId"/> is resolved by the caller
    /// through <see cref="GetExternalOrderId"/> and used verbatim so the order and its single line
    /// never diverge on identity.
    /// </summary>
    private ChannelOrderSnapshot MapToOrderSnapshot(WildberriesOrderDto dto, string externalOrderId)
    {
        DateTimeOffset? orderDate = ParseMoscowTimestamp(dto.Date);
        DateTimeOffset? changedAt = ParseMoscowTimestamp(dto.LastChangeDate);

        // The orders feed has no textual status field; isCancel/cancelDate are the only signals
        // Wildberries reports, so the snapshot status is reduced to these two values.
        string externalStatus = dto.IsCancel ? "cancelled" : "active";

        bool? isSellerWarehouse = dto.WarehouseType?.Contains("продавца", StringComparison.OrdinalIgnoreCase);

        ChannelOrderLineSnapshot line = new ChannelOrderLineSnapshot
        {
            LineKey = externalOrderId,
            ExternalLineId = dto.Srid,
            ExternalProductId = dto.NmId.ToString(CultureInfo.InvariantCulture),
            OfferId = dto.SupplierArticle,
            ExternalSizeId = dto.TechSize,
            ProductName = dto.Subject,
            Quantity = 1,
            UnitPrice = dto.PriceWithDisc,
            BuyerUnitPrice = dto.FinishedPrice,
            UnitPriceBeforeDiscount = dto.TotalPrice,
            Currency = WildberriesCurrency
        };

        return new ChannelOrderSnapshot
        {
            ExternalOrderId = externalOrderId,
            ExternalOrderNumber = dto.GNumber,
            OrderDate = orderDate ?? DateTimeOffset.UtcNow,
            StatusChangedAt = changedAt,
            ExternalStatus = externalStatus,
            ExternalSubstatus = dto.OrderType,
            CancelReason = dto.IsCancel ? "cancel" : null,
            Currency = WildberriesCurrency,
            DeliveryRegion = dto.RegionName,
            WarehouseName = dto.WarehouseName,
            IsSellerWarehouse = isSellerWarehouse,
            ChannelReportedTotal = dto.TotalPrice,
            Lines = new List<ChannelOrderLineSnapshot> { line }
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
