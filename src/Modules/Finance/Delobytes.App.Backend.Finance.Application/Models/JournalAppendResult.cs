namespace Delobytes.App.Backend.Finance.Application.Models;

/// <summary>
/// Counters of one journal append, as reported to the run that produced the batch.
/// </summary>
/// <param name="Appended">Entries written to the journal.</param>
/// <param name="Duplicates">Entries skipped because the idempotency key was already present.</param>
/// <param name="Superseded">Existing entries replaced by a newer report instance.</param>
/// <param name="Rejected">Entries with a negative amount or no attribution identity at all.</param>
/// <param name="Unattributed">Appended entries that matched no order line yet.</param>
/// <param name="UnmatchedInstanceRows">
/// Entries of a new report instance that matched no current entry of the same report, which is the
/// signal that row-level supersede stopped working because the row identity is not stable.
/// </param>
/// <param name="AffectedOrderLineIds">Order lines whose P&amp;L totals must be recomputed.</param>
public sealed record JournalAppendResult(
    int Appended,
    int Duplicates,
    int Superseded,
    int Rejected,
    int Unattributed,
    int UnmatchedInstanceRows,
    IReadOnlyCollection<Guid> AffectedOrderLineIds);
