namespace Delobytes.App.Backend.Integrations.Application.Models;

/// <summary>
/// Well-known values for <see cref="Interfaces.RateLimitScope.Method"/>, one per orders API method
/// that is paced independently.
/// </summary>
public static class OrdersRateLimiterMethods
{
    /// <summary>
    /// Ozon <c>POST /v3/posting/fbo/list</c>.
    /// </summary>
    public const string OzonPostingFboList = "posting/fbo/list";

    /// <summary>
    /// Ozon <c>POST /v4/posting/fbs/list</c>. Reserved: FBS is not implemented yet.
    /// </summary>
    public const string OzonPostingFbsList = "posting/fbs/list";

    /// <summary>
    /// Wildberries <c>GET /api/v1/supplier/orders</c>.
    /// </summary>
    public const string WildberriesSupplierOrders = "supplier/orders";
}
