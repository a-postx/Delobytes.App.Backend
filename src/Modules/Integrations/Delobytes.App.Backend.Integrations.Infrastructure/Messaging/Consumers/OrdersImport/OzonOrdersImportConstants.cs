namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers.OrdersImport;

/// <summary>
/// Shared tuning constants for the Ozon orders import, kept alongside the generic
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

    /// <summary>
    /// The FBS posting statuses. Deliberately longer than <see cref="AllFboStatuses"/>: a
    /// seller-fulfilled posting walks through registration, approval and handover states that have
    /// no FBO counterpart, and every one of them is a state the order must be seen in.
    /// </summary>
    public static readonly IReadOnlyList<string> AllFbsStatuses = new List<string>
    {
        "awaiting_registration",
        "acceptance_in_progress",
        "awaiting_approve",
        "awaiting_packaging",
        "awaiting_deliver",
        "arbitration",
        "client_arbitration",
        "delivering",
        "driver_pickup",
        "delivered",
        "cancelled",
        "not_accepted",
        "sent_by_seller",
    };
}
