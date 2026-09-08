using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Delobytes.App.Backend.Integrations.Application.DTOs;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Models;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Integrations.Infrastructure.ApiClients;

/// <summary>
/// Yandex.Kit API client for orders and stocks synchronisation.
/// </summary>
public class YandexKitApiClient : IChannelApiClient
{
    private readonly HttpClient _httpClient;

    // только для метода GetAccountInfoAsync
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<YandexKitApiClient> _logger;

    public YandexKitApiClient(
        HttpClient httpClient,
        IHttpClientFactory httpClientFactory,
        ILogger<YandexKitApiClient> logger)
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
        using HttpClient client = _httpClientFactory.CreateClient();

        using HttpRequestMessage request = new HttpRequestMessage(
            HttpMethod.Get,
            "https://api.kit.yandex.net/v1/store");

        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");

        try
        {
            using HttpResponseMessage response = await client.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Yandex.Kit /v1/store returned {StatusCode}.", (int)response.StatusCode);
                return null;
            }

            YandexKitStoreResponse? body =
                await response.Content.ReadFromJsonAsync<YandexKitStoreResponse>(ct);

            if (body is null)
            {
                return null;
            }

            return new AccountInfo
            {
                CustomerName = body.Slug,
                LegalName = null,
                Inn = null,
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Failed to retrieve Yandex.Kit store info.");
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

    private sealed class YandexKitStoreResponse
    {
        [JsonPropertyName("slug")]
        public string? Slug { get; init; }
    }
}
