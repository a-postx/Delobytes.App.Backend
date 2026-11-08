namespace Delobytes.App.Backend.Integrations.Application.Options;

/// <summary>
/// The resolved, validated view of one channel's orders-import settings. Every value is already
/// effective: no sentinel can reach a consumer, and no consumer re-applies a default.
/// </summary>
public sealed record OrdersImportTuning
{
    /// <summary>
    /// Number of orders published in one batch. The Sales side consumes batches, so the size is a
    /// property of the import pipeline rather than of any single channel.
    /// </summary>
    public const int DefaultBatchSize = 30;

    public required TimeSpan RunBudget { get; init; }

    public required TimeSpan MaxInProcessWait { get; init; }

    public required int MaxRequestsPerRun { get; init; }

    public required int BatchSize { get; init; }

    public required TimeSpan SafetyOverlap { get; init; }

    public required TimeSpan BackfillWindow { get; init; }

    public required TimeSpan MinOrdersWindow { get; init; }

    public required int? PageSize { get; init; }

    public required string MethodName { get; init; }
}
