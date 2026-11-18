using Delobytes.App.Backend.Finance.Application.Models;
using Delobytes.App.Backend.Finance.Domain.Entities;

namespace Delobytes.App.Backend.Finance.Application.Attribution;

/// <summary>
/// Attributes journal entries to Sales order lines. Pure: it sees only the entries and the
/// projections the caller loaded, so the same rule can be exercised without a database.
/// </summary>
/// <remarks>
/// The rules are shared by both channels. An entry with a unit identity is matched by
/// <c>(ChannelId, ExternalLineId)</c> and, when several lines share it (a multi-product Ozon
/// posting), narrowed by the product. An entry without a unit identity (an Ozon <c>ITEM</c> accrual)
/// is matched by <c>(ChannelId, ExternalOrderId, ExternalProductId)</c>; when several lines match,
/// the one with the ordinally smallest external line identity wins and the match is counted as
/// ambiguous, so the choice is deterministic across recomputes.
/// </remarks>
public static class PnlEntryAttribution
{
    /// <summary>
    /// Attributes <paramref name="entries"/> to <paramref name="orderLines"/>.
    /// </summary>
    /// <param name="entries">Journal entries to attribute.</param>
    /// <param name="orderLines">Order line projections available for attribution.</param>
    /// <returns>The matches, the entries that matched nothing and the ambiguity counter.</returns>
    public static PnlEntryAttributionResult Attribute(
        IReadOnlyCollection<PnlEntry> entries,
        IReadOnlyCollection<OrderLineRef> orderLines)
    {
        ArgumentNullException.ThrowIfNull(orderLines);

        List<AttributionCandidate> candidates = orderLines
            .Select(line => new AttributionCandidate(
                line.OrderLineId,
                line.ChannelId,
                line.ExternalOrderId,
                line.ExternalLineId,
                line.ExternalProductId))
            .ToList();

        return Attribute(entries, candidates);
    }

    /// <summary>
    /// Attributes <paramref name="entries"/> to the order line facts a caller projected.
    /// </summary>
    /// <param name="entries">Journal entries to attribute.</param>
    /// <param name="orderLines">Order line facts available for attribution.</param>
    /// <returns>The matches, the entries that matched nothing and the ambiguity counter.</returns>
    public static PnlEntryAttributionResult Attribute(
        IReadOnlyCollection<PnlEntry> entries,
        IReadOnlyCollection<AttributionCandidate> orderLines)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(orderLines);

        Dictionary<(Guid ChannelId, string ExternalLineId), List<AttributionCandidate>> byLine = new();
        Dictionary<(Guid ChannelId, string ExternalOrderId, string ExternalProductId), List<AttributionCandidate>> byOrder = new();

        foreach (AttributionCandidate line in orderLines)
        {
            if (!string.IsNullOrEmpty(line.ExternalLineId))
            {
                (Guid ChannelId, string ExternalLineId) lineKey = (line.ChannelId, line.ExternalLineId);

                if (!byLine.TryGetValue(lineKey, out List<AttributionCandidate>? lineBucket))
                {
                    lineBucket = new List<AttributionCandidate>();
                    byLine.Add(lineKey, lineBucket);
                }

                lineBucket.Add(line);
            }

            (Guid ChannelId, string ExternalOrderId, string ExternalProductId) orderKey = (
                line.ChannelId,
                line.ExternalOrderId,
                line.ExternalProductId);

            if (!byOrder.TryGetValue(orderKey, out List<AttributionCandidate>? orderBucket))
            {
                orderBucket = new List<AttributionCandidate>();
                byOrder.Add(orderKey, orderBucket);
            }

            orderBucket.Add(line);
        }

        List<PnlEntryAttributionMatch> matches = new();
        List<PnlEntry> unmatched = new();
        int ambiguous = 0;

        foreach (PnlEntry entry in entries)
        {
            if (!string.IsNullOrEmpty(entry.ExternalLineId))
            {
                AttributionCandidate? line = ResolveLineEntry(entry, byLine);

                if (line == null)
                {
                    unmatched.Add(entry);
                }
                else
                {
                    matches.Add(new PnlEntryAttributionMatch(line.OrderLineId, entry));
                }

                continue;
            }

            if (string.IsNullOrEmpty(entry.ExternalOrderId))
            {
                unmatched.Add(entry);
                continue;
            }

            (Guid ChannelId, string ExternalOrderId, string ExternalProductId) lookupKey = (
                entry.ChannelId,
                entry.ExternalOrderId,
                entry.ExternalProductId);

            if (!byOrder.TryGetValue(lookupKey, out List<AttributionCandidate>? candidates) || candidates.Count == 0)
            {
                unmatched.Add(entry);
                continue;
            }

            AttributionCandidate chosen;

            if (candidates.Count == 1)
            {
                chosen = candidates[0];
            }
            else
            {
                chosen = PickSmallestExternalLineId(candidates);
                ambiguous++;
            }

            matches.Add(new PnlEntryAttributionMatch(chosen.OrderLineId, entry));
        }

        return new PnlEntryAttributionResult(matches, unmatched, ambiguous);
    }

    private static AttributionCandidate? ResolveLineEntry(
        PnlEntry entry,
        Dictionary<(Guid ChannelId, string ExternalLineId), List<AttributionCandidate>> byLine)
    {
        (Guid ChannelId, string ExternalLineId) key = (entry.ChannelId, entry.ExternalLineId!);

        if (!byLine.TryGetValue(key, out List<AttributionCandidate>? candidates) || candidates.Count == 0)
        {
            return null;
        }

        if (candidates.Count == 1)
        {
            return candidates[0];
        }

        List<AttributionCandidate> narrowed = candidates
            .Where(l => string.Equals(l.ExternalProductId, entry.ExternalProductId, StringComparison.Ordinal))
            .ToList();

        if (narrowed.Count == 0)
        {
            return null;
        }

        if (narrowed.Count == 1)
        {
            return narrowed[0];
        }

        // Several lines of the same unit and product: keep the choice deterministic instead of
        // depending on query order, so a recompute attributes the entry the same way every time.
        return narrowed
            .OrderBy(l => l.OrderLineId)
            .First();
    }

    private static AttributionCandidate PickSmallestExternalLineId(List<AttributionCandidate> candidates)
    {
        return candidates
            .OrderBy(l => l.ExternalLineId, StringComparer.Ordinal)
            .ThenBy(l => l.OrderLineId)
            .First();
    }
}
