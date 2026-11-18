namespace Delobytes.App.Backend.Integrations.Application.Models;

/// <summary>
/// Source for the Wildberries API methods that are paced independently.
/// Adding a method is one constant here plus one <see cref="Interfaces.RateLimitScope"/> at the
/// call site.
/// </summary>
public static class WildberriesApiMethodRegistry
{
    /// <summary>
    /// <c>GET /api/v1/supplier/orders</c> — orders feed used by the orders import.
    /// </summary>
    public const string SupplierOrders = "supplier/orders";

    /// <summary>
    /// <c>POST /content/v2/get/cards/list</c> — paginated product cards used by the products import.
    /// </summary>
    public const string ContentCardsList = "content/cards/list";

    /// <summary>
    /// <c>GET /api/v1/seller-info</c> — seller account details used during connection validation.
    /// </summary>
    public const string SellerInfo = "seller-info";

    /// <summary>
    /// <c>POST /api/finance/v1/sales-reports/detailed</c> — weekly detailed sales report used by
    /// the finance import.
    /// </summary>
    public const string FinanceSalesReportsDetailed = "finance/sales-reports/detailed";

    private static readonly HashSet<string> KnownMethods = new HashSet<string>(StringComparer.Ordinal)
    {
        SupplierOrders,
        ContentCardsList,
        SellerInfo,
        FinanceSalesReportsDetailed,
    };

    /// <summary>
    /// Gets every registered method name. Exposed read-only so a caller cannot widen the set the
    /// limiter validates against.
    /// </summary>
    public static IReadOnlySet<string> All { get; } = KnownMethods;

    /// <summary>
    /// Returns <c>true</c> when <paramref name="method"/> is a registered method.
    /// </summary>
    public static bool IsKnown(string method)
    {
        return KnownMethods.Contains(method);
    }
}

