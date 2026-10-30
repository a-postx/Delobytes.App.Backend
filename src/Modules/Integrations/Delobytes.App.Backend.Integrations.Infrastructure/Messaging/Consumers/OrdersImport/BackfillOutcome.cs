namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers.OrdersImport;

/// <summary>
/// Collected result of one backfill run: how many days and orders the walk covered before stopping
/// at the end of the scope or at a throttle/budget stop recorded on the run state.
/// </summary>
public sealed record BackfillOutcome
{
    public required int TotalOrders { get; init; }

    public required int DaysFetched { get; init; }

    public required DateTimeOffset? FirstDay { get; init; }

    public required DateTimeOffset? LastDay { get; init; }
}
