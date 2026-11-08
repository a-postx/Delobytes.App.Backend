namespace Delobytes.App.Backend.Integrations.Application.Orders;

/// <summary>
/// The two limits a run may hit: a wall-clock deadline and a hard cap on issued requests. Budget
/// decisions read this value, never the accumulating <see cref="RunCounters"/>.
/// </summary>
public sealed record RunBudget(DateTimeOffset Deadline, int MaxRequestsPerRun);
