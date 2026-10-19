namespace Delobytes.App.Backend.Integrations.Domain.Enums;

/// <summary>
/// Wildberries API methods that are rate-limited independently of each other. Distinct from
/// <see cref="ChannelEndpointType"/>, which groups endpoints by base URL: the rate limit applies
/// per path, and several methods (e.g. <see cref="SupplierOrders"/>) share a base URL with other
/// methods that carry a different allowance.
/// </summary>
public enum WildberriesApiMethod
{
    /// <summary>
    /// <c>GET /api/v1/supplier/orders</c>.
    /// </summary>
    SupplierOrders = 0,

    /// <summary>
    /// <c>POST /content/v2/get/cards/list</c>.
    /// </summary>
    ContentCardsList = 1,

    /// <summary>
    /// <c>GET /api/v1/seller-info</c>.
    /// </summary>
    SellerInfo = 2
}
