namespace Delobytes.App.Backend.Integrations.Domain.Enums;

/// <summary>
/// Defines types of channel API endpoints for different operation categories.
/// </summary>
public enum ChannelEndpointType
{
    /// <summary>
    /// General/common API operations (e.g., seller info, authentication).
    /// </summary>
    Common = 0,

    /// <summary>
    /// Content and catalog operations (products, listings).
    /// </summary>
    Content = 1,

    /// <summary>
    /// Orders and fulfillment operations.
    /// </summary>
    Orders = 2,

    /// <summary>
    /// Statistics and reports.
    /// </summary>
    Statistics = 3,

    /// <summary>
    /// Pricing operations.
    /// </summary>
    Prices = 4,

    /// <summary>
    /// Promotions and advertising campaigns.
    /// </summary>
    Promotions = 5,

    /// <summary>
    /// Seller analytics.
    /// </summary>
    Analytics = 6,

    /// <summary>
    /// Suppliers-specific operations.
    /// </summary>
    Suppliers = 7
}
