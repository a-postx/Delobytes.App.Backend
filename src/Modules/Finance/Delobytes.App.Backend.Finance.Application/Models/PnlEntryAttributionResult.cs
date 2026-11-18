using Delobytes.App.Backend.Finance.Domain.Entities;

namespace Delobytes.App.Backend.Finance.Application.Models;

/// <summary>
/// The outcome of attributing a set of journal entries to order lines.
/// </summary>
/// <param name="Matches">Every entry that was attributed to a line, one match per attribution.</param>
/// <param name="UnmatchedEntries">
/// Entries that matched no line. They stay in the journal and are picked up when the line arrives.
/// </param>
/// <param name="AmbiguousAttributions">
/// Number of Ozon <c>ITEM</c> entries that matched several lines and were attributed to the one with
/// the ordinally smallest external line identity.
/// </param>
public sealed record PnlEntryAttributionResult(
    IReadOnlyList<PnlEntryAttributionMatch> Matches,
    IReadOnlyList<PnlEntry> UnmatchedEntries,
    int AmbiguousAttributions);
