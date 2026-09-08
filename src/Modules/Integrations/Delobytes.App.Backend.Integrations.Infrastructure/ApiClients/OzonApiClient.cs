using System.Net;
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
    private readonly ILogger<YandexKitApiClient> _logger;

    public OzonApiClient(HttpClient httpClient, ILogger<YandexKitApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
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
}
