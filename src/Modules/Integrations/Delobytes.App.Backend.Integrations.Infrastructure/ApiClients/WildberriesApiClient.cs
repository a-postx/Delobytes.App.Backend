using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Delobytes.App.Backend.Integrations.Application.DTOs;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Contracts.Models;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Integrations.Infrastructure.ApiClients;

/// <summary>
/// Wildberries marketplace API client implementation.
/// </summary>
public class WildberriesApiClient : IChannelApiClient
{
    private readonly HttpClient _httpClient;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IEndpointResolver _endpointResolver;
    private readonly ILogger<WildberriesApiClient> _logger;

    private Guid? _templateId;

    /// <summary>
    /// Initializes a new instance of the <see cref="WildberriesApiClient"/> class.
    /// </summary>
    public WildberriesApiClient(
        HttpClient httpClient,
        IHttpClientFactory httpClientFactory,
        IEndpointResolver endpointResolver,
        ILogger<WildberriesApiClient> logger)
    {
        _httpClient = httpClient;
        _httpClientFactory = httpClientFactory;
        _endpointResolver = endpointResolver;
        _logger = logger;
    }

    /// <summary>
    /// Sets the template ID for this client instance.
    /// Must be called before any API operations.
    /// </summary>
    /// <param name="templateId">System channel template identifier.</param>
    public void SetTemplateId(Guid templateId)
    {
        _templateId = templateId;
    }

    /// <inheritdoc/>
    public async Task<AccountInfo?> GetAccountInfoAsync(
        string apiKey,
        string? apiSecret,
        Dictionary<string, string>? settings,
        CancellationToken ct)
    {
        if (_templateId == null)
        {
            throw new InvalidOperationException(
                "Template ID must be set before calling API methods. Call SetTemplateId() first.");
        }

        string baseUrl = await _endpointResolver.GetEndpointUrlAsync(
            _templateId.Value,
            ChannelEndpointType.Common,
            ct);

        using HttpClient client = _httpClientFactory.CreateClient();

        using HttpRequestMessage request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{baseUrl}/api/v1/seller-info");

        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");

        try
        {
            using HttpResponseMessage response = await client.SendAsync(request, ct);

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
    public Task<ApiResponse<OrdersData>> GetOrdersAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        ApiResponse<OrdersData> response = new ApiResponse<OrdersData>
        {
            IsSuccess = true,
            Data = new OrdersData
            {
                Orders = new List<OrderItem>(),
                TotalCount = 0
            },
            StatusCode = 200,
            Timestamp = DateTimeOffset.UtcNow
        };

        return Task.FromResult(response);
    }

    /// <inheritdoc/>
    public Task<ApiResponse<StocksData>> GetStocksAsync(CancellationToken ct)
    {
        ApiResponse<StocksData> response = new ApiResponse<StocksData>
        {
            IsSuccess = true,
            Data = new StocksData
            {
                Stocks = new List<StockItem>(),
                TotalCount = 0
            },
            StatusCode = 200,
            Timestamp = DateTimeOffset.UtcNow
        };

        return Task.FromResult(response);
    }

    /// <inheritdoc/>
    public async Task<ApiResponse<ProductCardsData>> GetProductCardsAsync(
        ProductCardsCursor? cursor,
        int limit,
        CancellationToken ct)
    {
        if (_templateId == null)
        {
            throw new InvalidOperationException(
                "Template ID must be set before calling API methods. Call SetTemplateId() first.");
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

        try
        {
            string requestJson = JsonSerializer.Serialize(requestBody, new JsonSerializerOptions
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });

            StringContent content = new StringContent(requestJson, Encoding.UTF8, "application/json");

            using HttpRequestMessage request = new HttpRequestMessage(
                HttpMethod.Post,
                $"{baseUrl}/content/v2/get/cards/list")
            {
                Content = content
            };

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
                    "Wildberries cards API returned {StatusCode}. Authentication failed.",
                    (int)response.StatusCode);

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
                    responseBody.Length > 500 ? responseBody.Substring(0, 500) : responseBody);

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
            ChannelSpecificData = channelSpecificData
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
}
