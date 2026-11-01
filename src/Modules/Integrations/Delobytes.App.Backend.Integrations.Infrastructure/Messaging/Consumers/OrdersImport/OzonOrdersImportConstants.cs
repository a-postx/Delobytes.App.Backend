namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers.OrdersImport;

/// <summary>
/// Shared tuning constants for the Ozon FBO orders import, kept alongside the generic
/// <see cref="OrdersImportConstants"/> because they are specific to the Ozon schema.
/// </summary>
internal static class OzonOrdersImportConstants
{
    /// <summary>
    /// The five FBO posting statuses, requested explicitly so a delta run's narrower window is
    /// never read as "no filter" by Ozon.
    /// </summary>
    public static readonly IReadOnlyList<string> AllFboStatuses = new List<string>
    {
        "awaiting_packaging",
        "awaiting_deliver",
        "delivering",
        "delivered",
        "cancelled",
    };
}
