namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers;

/// <summary>
/// Result of one time-budgeted backfill run: the union of the days it managed to fetch, how many
/// days that was, and why the day loop ended. The stopping reason is reported for the run log and
/// for nothing else — the cursor on <c>OrdersBackfillState</c>, not this record, decides where the
/// next run resumes.
/// </summary>
internal sealed record BackfillOutcome
{
    /// <summary>
    /// Gets the total number of orders published across every day fetched by this run. Each day
    /// is published as its own unit of work as soon as it is fetched (see
    /// <c>ProcessOrdersImportConsumer.FetchBackfillDaysAsync</c>), so this record no longer carries
    /// the orders themselves.
    /// </summary>
    public required int TotalOrders { get; init; }

    /// <summary>
    /// Gets the number of calendar days fetched, which equals the number of HTTP requests issued:
    /// one request per day is the whole point of the day-by-day walk.
    /// </summary>
    public required int DaysFetched { get; init; }

    /// <summary>
    /// Gets the number of rows the source refused to map for lack of identity. Non-zero always
    /// failed the run before this record was produced; it is carried so the failure is visible in
    /// the run counters.
    /// </summary>
    public required int RowsWithoutIdentity { get; init; }

    /// <summary>
    /// Gets a value indicating whether the limiter or a <c>429</c> ended the loop before the scope
    /// boundary was reached.
    /// </summary>
    public required bool StoppedByThrottle { get; init; }

    /// <summary>
    /// Gets a value indicating whether the run's wall-clock budget ended the loop.
    /// </summary>
    public required bool StoppedByBudget { get; init; }

    /// <summary>
    /// Gets the newest day fetched by this run, or null when it fetched none.
    /// </summary>
    public required DateTimeOffset? FirstDay { get; init; }

    /// <summary>
    /// Gets the oldest day fetched by this run, or null when it fetched none.
    /// </summary>
    public required DateTimeOffset? LastDay { get; init; }

    /// <summary>
    /// Gets a value indicating whether the run changed anything worth persisting on the cursor:
    /// it fetched at least one day, or it found the walk already past the scope boundary and
    /// completed it. Without the second case a cursor that starts past its own boundary would stay
    /// unfinished forever, because a run that fetched nothing would never write it.
    /// </summary>
    public required bool IsStateChanged { get; init; }
}
