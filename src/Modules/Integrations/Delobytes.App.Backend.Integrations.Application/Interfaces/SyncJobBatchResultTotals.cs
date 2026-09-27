namespace Delobytes.App.Backend.Integrations.Application.Interfaces;

/// <summary>
/// Aggregated counters across all batch results for a single SyncJob.
/// </summary>
public sealed class SyncJobBatchResultTotals
{
    public int TotalProcessed { get; init; }
    public int TotalCreated { get; init; }
    public int TotalUpdated { get; init; }
    public int TotalSkipped { get; init; }
    public int TotalFailed { get; init; }
    public string? CombinedErrors { get; init; }
}
