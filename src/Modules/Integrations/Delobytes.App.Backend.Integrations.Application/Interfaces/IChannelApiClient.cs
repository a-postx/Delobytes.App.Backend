using Delobytes.App.Backend.Integrations.Application.DTOs;
using Delobytes.App.Backend.Integrations.Application.Models;

namespace Delobytes.App.Backend.Integrations.Application.Interfaces;

/// <summary>
/// Defines the contract for a selling channel API client.
/// </summary>
public interface IChannelApiClient
{
    /// <summary>
    /// Retrieves account/shop information for the supplied credentials.
    /// Returns null when the marketplace does not expose this information or the call fails.
    /// </summary>
    /// <param name="apiKey">API key.</param>
    /// <param name="apiSecret">Optional API secret.</param>
    /// <param name="settings">Optional extra settings (e.g. sellerId for Ozon).</param>
    /// <param name="ct">Cancellation token.</param>
    Task<AccountInfo?> GetAccountInfoAsync(
        string apiKey,
        string? apiSecret,
        Dictionary<string, string>? settings,
        CancellationToken ct);

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
