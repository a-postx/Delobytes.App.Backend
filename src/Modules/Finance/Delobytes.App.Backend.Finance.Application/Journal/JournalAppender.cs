using Delobytes.App.Backend.Finance.Application.Attribution;
using Delobytes.App.Backend.Finance.Application.Interfaces;
using Delobytes.App.Backend.Finance.Application.Models;
using Delobytes.App.Backend.Finance.Domain.Entities;
using Delobytes.App.Backend.Finance.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Finance.Application.Journal;

/// <summary>
/// Appends channel money facts to the journal. Every rejection, duplicate and supersede is counted,
/// because the point of the journal is to make missing money visible rather than to drop rows
/// silently.
/// </summary>
/// <remarks>
/// The journal is append-only: a correction never overwrites a row, it supersedes it. The only
/// supersede mechanism is row-level and driven by a newer report instance, because there is no
/// outbox and no report-level revision number to rely on.
/// </remarks>
public class JournalAppender
{
    private readonly IPnlJournalStore _journal;
    private readonly IOrderLineRefStore _orderLineRefs;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<JournalAppender> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="JournalAppender"/> class.
    /// </summary>
    /// <param name="journal">Journal persistence port.</param>
    /// <param name="orderLineRefs">Order line projection port.</param>
    /// <param name="timeProvider">Clock source.</param>
    /// <param name="logger">Logger instance.</param>
    public JournalAppender(
        IPnlJournalStore journal,
        IOrderLineRefStore orderLineRefs,
        TimeProvider timeProvider,
        ILogger<JournalAppender> logger)
    {
        _journal = journal;
        _orderLineRefs = orderLineRefs;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Appends the entries of one batch, supersedes the facts they replace and attributes what was
    /// written.
    /// </summary>
    /// <param name="entries">Entries to append.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The counters of the append and the order lines to recompute.</returns>
    public async Task<JournalAppendResult> AppendAsync(
        IReadOnlyCollection<PnlEntry> entries,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entries);

        DateTimeOffset now = _timeProvider.GetUtcNow();

        List<PnlEntry> accepted = new();
        HashSet<JournalIdentityKey> batchKeys = new();
        int rejected = 0;
        int duplicates = 0;

        foreach (PnlEntry entry in entries)
        {
            if (entry.Amount < 0m || (string.IsNullOrEmpty(entry.ExternalLineId) && string.IsNullOrEmpty(entry.ExternalOrderId)))
            {
                rejected++;
                continue;
            }

            JournalIdentityKey key = IdentityKeyOf(entry);

            if (!batchKeys.Add(key))
            {
                duplicates++;
                continue;
            }

            accepted.Add(entry);
        }

        List<JournalIdentityKey> identityKeys = accepted.Select(IdentityKeyOf).ToList();

        IReadOnlyList<PnlEntry> existing = await _journal.LoadByIdentityAsync(identityKeys, cancellationToken);

        if (existing.Count > 0)
        {
            HashSet<JournalIdentityKey> existingKeys = existing.Select(IdentityKeyOf).ToHashSet();

            List<PnlEntry> unique = new();

            foreach (PnlEntry entry in accepted)
            {
                if (existingKeys.Contains(IdentityKeyOf(entry)))
                {
                    duplicates++;
                    continue;
                }

                unique.Add(entry);
            }

            accepted = unique;
        }

        foreach (PnlEntry entry in accepted)
        {
            if (entry.Id == Guid.Empty)
            {
                entry.Id = Guid.NewGuid();
            }

            if (entry.ImportedAt == default)
            {
                entry.ImportedAt = now;
            }

            if (entry.CreatedAt == default)
            {
                entry.CreatedAt = now;
            }
        }

        (List<SupersedeInstruction> supersedes, List<PnlEntry> supersededEntries) =
            await PlanSupersedesAsync(accepted, now, cancellationToken);

        int unmatchedInstanceRows = await CountUnmatchedInstanceRowsAsync(accepted, cancellationToken);

        if (accepted.Count > 0)
        {
            await _journal.AppendAsync(accepted, cancellationToken);
        }

        if (supersedes.Count > 0)
        {
            await _journal.SupersedeAsync(supersedes, cancellationToken);
        }

        List<PnlEntry> attributionCandidates = new(accepted);
        attributionCandidates.AddRange(supersededEntries);

        HashSet<Guid> affectedLineIds = await ResolveAffectedLinesAsync(attributionCandidates, cancellationToken);

        int unattributed = await CountUnattributedAsync(accepted, cancellationToken);

        return new JournalAppendResult(
            accepted.Count,
            duplicates,
            supersededEntries.Count,
            rejected,
            unattributed,
            unmatchedInstanceRows,
            affectedLineIds);
    }

    private async Task<(List<SupersedeInstruction> Instructions, List<PnlEntry> SupersededEntries)> PlanSupersedesAsync(
        List<PnlEntry> accepted,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (accepted.Count == 0)
        {
            return (new List<SupersedeInstruction>(), new List<PnlEntry>());
        }

        Dictionary<JournalComparisonKey, PnlEntry> newestAccepted = new();

        foreach (PnlEntry entry in accepted)
        {
            JournalComparisonKey key = ComparisonKeyOf(entry);

            if (newestAccepted.TryGetValue(key, out PnlEntry? currentNewest)
                && InstanceOrdering(entry.ReportInstanceKey) > InstanceOrdering(currentNewest.ReportInstanceKey))
            {
                newestAccepted[key] = entry;
            }
            else if (!newestAccepted.ContainsKey(key))
            {
                newestAccepted.Add(key, entry);
            }
        }

        IReadOnlyList<PnlEntry> current = await _journal.LoadCurrentByComparisonAsync(
            newestAccepted.Keys.ToList(),
            cancellationToken);

        List<SupersedeInstruction> instructions = new();
        List<PnlEntry> supersededEntries = new();

        foreach (PnlEntry oldEntry in current)
        {
            JournalComparisonKey key = ComparisonKeyOf(oldEntry);

            if (!newestAccepted.TryGetValue(key, out PnlEntry? replacement))
            {
                continue;
            }

            if (string.Equals(oldEntry.ReportInstanceKey, replacement.ReportInstanceKey, StringComparison.Ordinal))
            {
                continue;
            }

            if (InstanceOrdering(oldEntry.ReportInstanceKey) >= InstanceOrdering(replacement.ReportInstanceKey))
            {
                continue;
            }

            instructions.Add(new SupersedeInstruction(oldEntry.Id, replacement.Id, now));
            supersededEntries.Add(oldEntry);
        }

        return (instructions, supersededEntries);
    }

    private async Task<int> CountUnmatchedInstanceRowsAsync(
        List<PnlEntry> accepted,
        CancellationToken cancellationToken)
    {
        // Only a Wildberries report is re-issued; an Ozon accrual is never repeated.
        List<PnlEntry> reportEntries = accepted
            .Where(e => e.Source == PnlSource.WbReport && !string.IsNullOrEmpty(e.ReportInstanceKey))
            .ToList();

        if (reportEntries.Count == 0)
        {
            return 0;
        }

        Dictionary<(Guid ChannelId, string ReportId), HashSet<string>> knownInstances = new();
        Dictionary<JournalComparisonKey, bool> matchedComparisonKeys = new();

        IReadOnlyList<PnlEntry> byComparison = await _journal.LoadCurrentByComparisonAsync(
            reportEntries.Select(ComparisonKeyOf).Distinct().ToList(),
            cancellationToken);

        HashSet<JournalComparisonKey> existingComparisonKeys = byComparison.Select(ComparisonKeyOf).ToHashSet();

        int unmatched = 0;

        foreach (PnlEntry entry in reportEntries)
        {
            JournalComparisonKey comparisonKey = ComparisonKeyOf(entry);

            if (existingComparisonKeys.Contains(comparisonKey))
            {
                continue;
            }

            string reportId = ReportIdOf(entry.ReportInstanceKey);
            (Guid ChannelId, string ReportId) reportKey = (entry.ChannelId, reportId);

            if (!knownInstances.TryGetValue(reportKey, out HashSet<string>? instanceKeys))
            {
                IReadOnlyCollection<string> loaded = await _journal.LoadInstanceKeysAsync(
                    entry.ChannelId,
                    entry.Source,
                    reportId,
                    cancellationToken);

                instanceKeys = loaded.ToHashSet(StringComparer.Ordinal);
                knownInstances.Add(reportKey, instanceKeys);
            }

            // The first import of a report has no predecessor and is not a defect. A later instance
            // whose row matches nothing means the row identity is not stable.
            if (instanceKeys.Count == 0 || instanceKeys.Contains(entry.ReportInstanceKey))
            {
                continue;
            }

            _logger.LogWarning(
                "Finance report instance {ReportInstanceKey} produced rows that match no current entry. ReportId={ReportId}, ChannelId={ChannelId}",
                entry.ReportInstanceKey,
                reportId,
                entry.ChannelId);

            unmatched++;
        }

        return unmatched;
    }

    private async Task<HashSet<Guid>> ResolveAffectedLinesAsync(
        List<PnlEntry> entries,
        CancellationToken cancellationToken)
    {
        if (entries.Count == 0)
        {
            return new HashSet<Guid>();
        }

        List<OrderLineAttributionKey> keys = entries.Select(AttributionKeyOf).Distinct().ToList();

        IReadOnlyList<OrderLineRef> refs = await _orderLineRefs.LoadByAttributionAsync(keys, cancellationToken);

        PnlEntryAttributionResult attribution = PnlEntryAttribution.Attribute(entries, refs);

        return attribution.Matches.Select(m => m.OrderLineId).ToHashSet();
    }

    private async Task<int> CountUnattributedAsync(List<PnlEntry> entries, CancellationToken cancellationToken)
    {
        if (entries.Count == 0)
        {
            return 0;
        }

        List<OrderLineAttributionKey> keys = entries.Select(AttributionKeyOf).Distinct().ToList();

        IReadOnlyList<OrderLineRef> refs = await _orderLineRefs.LoadByAttributionAsync(keys, cancellationToken);

        return PnlEntryAttribution.Attribute(entries, refs).UnmatchedEntries.Count;
    }

    private static JournalIdentityKey IdentityKeyOf(PnlEntry entry)
    {
        return new JournalIdentityKey(
            entry.ChannelId,
            entry.Source,
            entry.ReportInstanceKey,
            entry.ExternalEntryId,
            entry.ExternalProductId,
            entry.Kind,
            entry.Type);
    }

    private static JournalComparisonKey ComparisonKeyOf(PnlEntry entry)
    {
        return new JournalComparisonKey(
            entry.Source,
            entry.ExternalEntryId,
            entry.ExternalProductId,
            entry.Kind,
            entry.Type);
    }

    private static OrderLineAttributionKey AttributionKeyOf(PnlEntry entry)
    {
        if (!string.IsNullOrEmpty(entry.ExternalLineId))
        {
            return OrderLineAttributionKey.ForLine(entry.ChannelId, entry.ExternalLineId);
        }

        return OrderLineAttributionKey.ForOrder(entry.ChannelId, entry.ExternalOrderId!, entry.ExternalProductId);
    }

    private static string ReportIdOf(string reportInstanceKey)
    {
        int separator = reportInstanceKey.IndexOf(':', StringComparison.Ordinal);

        return separator > 0
            ? reportInstanceKey[..separator]
            : reportInstanceKey;
    }

    private static DateOnly InstanceOrdering(string reportInstanceKey)
    {
        int separator = reportInstanceKey.IndexOf(':', StringComparison.Ordinal);

        if (separator >= 0
            && DateOnly.TryParse(
                reportInstanceKey[(separator + 1)..],
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out DateOnly parsed))
        {
            return parsed;
        }

        return DateOnly.MinValue;
    }
}
