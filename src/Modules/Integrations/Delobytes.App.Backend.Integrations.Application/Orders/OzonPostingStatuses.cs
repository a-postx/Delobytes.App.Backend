namespace Delobytes.App.Backend.Integrations.Application.Orders;

/// <summary>
/// Posting statuses of the Ozon orders endpoints. Channel vocabulary rather than an HTTP detail:
/// the FBS delta fetcher reads them, and it lives in this assembly.
/// </summary>
public static class OzonPostingStatuses
{
    /// <summary>
    /// The five FBO posting statuses, requested explicitly so a delta run's narrower window is
    /// never read as "no filter" by Ozon.
    /// </summary>
    public static readonly IReadOnlyList<string> FboStatuses = new List<string>
    {
        "awaiting_packaging",
        "awaiting_deliver",
        "delivering",
        "delivered",
        "cancelled",
    };

    /// <summary>
    /// The FBS posting statuses. Deliberately longer than <see cref="FboStatuses"/>: a
    /// seller-fulfilled posting walks through registration, approval and handover states that have
    /// no FBO counterpart, and every one of them is a state the order must be seen in.
    /// </summary>
    public static readonly IReadOnlyList<string> FbsStatuses = new List<string>
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
    };
}
