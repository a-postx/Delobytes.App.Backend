namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers;

internal sealed record BackfillOutcome
{
    public required int TotalOrders { get; init; }

    public required int DaysFetched { get; init; }

    public required int RowsWithoutIdentity { get; init; }

    public required bool StoppedByThrottle { get; init; }

    public required bool StoppedByBudget { get; init; }

    public required DateTimeOffset? FirstDay { get; init; }

    public required DateTimeOffset? LastDay { get; init; }

    public DateTimeOffset? ResumeAt { get; init; }
}
