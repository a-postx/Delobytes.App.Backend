using Delobytes.App.Backend.Integrations.Application.DTOs;

namespace Delobytes.App.Backend.Integrations.Application.Interfaces;

/// <summary>
/// Defines the contract for a selling channel API client.
/// </summary>
public interface IChannelApiClient
{
    /// <summary>
    /// Retrieves orders within the specified date range.
    /// </summary>
    /// <param name="from">Start date.</param>
    /// <param name="to">End date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>API response containing orders data.</returns>
    Task<ApiResponse<OrdersData>> GetOrdersAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);

    /// <summary>
    /// Retrieves current stock information.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>API response containing stocks data.</returns>
    Task<ApiResponse<StocksData>> GetStocksAsync(CancellationToken ct);
}
