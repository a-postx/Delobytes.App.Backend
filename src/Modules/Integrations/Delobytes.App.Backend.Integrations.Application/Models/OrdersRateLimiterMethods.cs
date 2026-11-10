namespace Delobytes.App.Backend.Integrations.Application.Models;

/// <summary>
/// Well-known Ozon values for <see cref="Interfaces.RateLimitScope.Method"/>, one per orders API
/// method that is paced independently. Wildberries methods live in
/// <see cref="WildberriesApiMethodRegistry"/>, which covers every WB endpoint, not only orders.
/// </summary>
public static class OrdersRateLimiterMethods
{
    /// <summary>
    /// Ozon <c>POST /v3/posting/fbo/list</c>.
    /// </summary>
    public const string OzonPostingFboList = "posting/fbo/list";

    /// <summary>
    /// Ozon <c>POST /v4/posting/fbs/list</c>.
    /// </summary>
    public const string OzonPostingFbsList = "posting/fbs/list";
}
