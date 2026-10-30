namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers.OrdersImport;

/// <summary>
/// Collected result of one backfill run: how many days and orders the walk covered, and why it
/// stopped before reaching the end of the scope.
/// </summary>
internal sealed record BackfillOutcome
{
    public required int TotalOrders { get; init; }

    public required int DaysFetched { get; init; }

    public required bool StoppedByThrottle { get; init; }

    public required bool StoppedByBudget { get; init; }

    public required DateTimeOffset? FirstDay { get; init; }

    public required DateTimeOffset? LastDay { get; init; }

    public DateTimeOffset? ResumeAt { get; init; }
}
