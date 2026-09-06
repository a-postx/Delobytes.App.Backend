using Delobytes.App.Backend.Integrations.Application.DTOs;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Integrations.Infrastructure.ApiClients;

/// <summary>
/// Wildberries marketplace API client implementation.
/// </summary>
public class WildberriesApiClient : IChannelApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<WildberriesApiClient> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WildberriesApiClient"/> class.
    /// </summary>
    /// <param name="httpClient">HTTP client.</param>
    /// <param name="logger">Logger.</param>
    public WildberriesApiClient(HttpClient httpClient, ILogger<WildberriesApiClient> logger)
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
}
