namespace Delobytes.App.Backend.Finance.Application.Models;

/// <summary>
/// Counters of one P&amp;L recompute pass, reported so a batch failure is visible instead of
/// aborting the whole call.
/// </summary>
/// <param name="Succeeded">Order lines whose totals were recomputed and persisted.</param>
/// <param name="Failed">Order lines skipped because their totals could not be calculated; each
/// one's stored line, if any, was left exactly as it was before the pass.</param>
/// <param name="FailedOrderLineIds">Identifiers of the skipped order lines, for diagnostics.</param>
public sealed record PnlRecomputeResult(
    int Succeeded,
    int Failed,
    IReadOnlyCollection<Guid> FailedOrderLineIds);
