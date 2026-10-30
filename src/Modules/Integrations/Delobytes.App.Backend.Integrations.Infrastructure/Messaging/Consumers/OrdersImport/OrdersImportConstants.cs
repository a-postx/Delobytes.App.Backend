namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers.OrdersImport;

/// <summary>
/// Shared tuning constants for the orders import run.
/// </summary>
internal static class OrdersImportConstants
{
    public const int BatchSize = 30;

    /// <summary>
    /// Hard bound on the number of HTTP requests one run may issue. Bisection of a window does
    /// not loop, but the bound protects against a pathological configuration.
    /// </summary>
    public const int MaxRequestsPerRun = 1000;

    public static readonly TimeSpan SafetyOverlap = TimeSpan.FromMinutes(30);

    public static readonly TimeSpan BackfillWindow = TimeSpan.FromDays(180);

    public static readonly TimeSpan MoscowOffset = TimeSpan.FromHours(3);
}
