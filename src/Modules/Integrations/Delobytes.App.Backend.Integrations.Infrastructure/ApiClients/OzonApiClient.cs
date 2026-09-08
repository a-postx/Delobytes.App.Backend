using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using Delobytes.App.Backend.Integrations.Application.DTOs;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Models;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Integrations.Infrastructure.ApiClients;

/// <summary>
/// Ozon marketplace API client for orders and stocks synchronisation.
/// </summary>
public class OzonApiClient : IChannelApiClient
{
    private readonly HttpClient _httpClient;

    // только для метода GetAccountInfoAsync
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<OzonApiClient> _logger;

    public OzonApiClient(
        HttpClient httpClient,
        IHttpClientFactory httpClientFactory,
        ILogger<OzonApiClient> logger)
    {
        _httpClient = httpClient;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<AccountInfo?> GetAccountInfoAsync(
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
                CustomerName = body.Name,
                LegalName = body.LegalName,
                Inn = body.Inn,
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Failed to retrieve Ozon seller info.");
            return null;
        }
    }

    /// <inheritdoc/>
    public Task<ApiResponse<OrdersData>> GetOrdersAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        ApiResponse<OrdersData> response = new ApiResponse<OrdersData>
        {
            IsSuccess = true,
            Data = new OrdersData { Orders = new List<OrderItem>(), TotalCount = 0 },
            StatusCode = 200,
            Timestamp = DateTimeOffset.UtcNow,
        };

        return Task.FromResult(response);
    }

    /// <inheritdoc/>
    public Task<ApiResponse<StocksData>> GetStocksAsync(CancellationToken ct)
    {
        ApiResponse<StocksData> response = new ApiResponse<StocksData>
        {
            IsSuccess = true,
            Data = new StocksData { Stocks = new List<StockItem>(), TotalCount = 0 },
            StatusCode = 200,
            Timestamp = DateTimeOffset.UtcNow,
        };

        return Task.FromResult(response);
    }

    private sealed class OzonSellerInfoResponse
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("legal_name")]
        public string? LegalName { get; init; }

        [JsonPropertyName("inn")]
        public string? Inn { get; init; }
    }
}
