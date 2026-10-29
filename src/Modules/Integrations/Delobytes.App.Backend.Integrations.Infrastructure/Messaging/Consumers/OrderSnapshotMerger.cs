using Delobytes.App.Backend.Integrations.Contracts.Models;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers;

/// <summary>
/// Merges reports of the same order into one snapshot. Used by both the delta fetch (an
/// accumulator across pages of a bisected window) and the backfill walk (within one day).
/// </summary>
internal static class OrderSnapshotMerger
{
    /// <summary>
    /// Merges <paramref name="reports"/> by <see cref="ChannelOrderSnapshot.ExternalOrderId"/>.
    /// Returns insertion-ordered values; a caller that needs deterministic output (the backfill
    /// walk) applies its own ordering on the result.
    /// </summary>
    public static List<ChannelOrderSnapshot> MergeByExternalOrderId(IEnumerable<ChannelOrderSnapshot> reports)
    {
        Dictionary<string, ChannelOrderSnapshot> ordersByOrderKey = new Dictionary<string, ChannelOrderSnapshot>(StringComparer.Ordinal);

        foreach (ChannelOrderSnapshot order in reports)
        {
            if (ordersByOrderKey.TryGetValue(order.ExternalOrderId, out ChannelOrderSnapshot? existing))
            {
                ordersByOrderKey[order.ExternalOrderId] = Merge(existing, order);
            }
            else
            {
                ordersByOrderKey.Add(order.ExternalOrderId, order);
            }
        }

        return ordersByOrderKey.Values.ToList();
    }

    /// <summary>
    /// "Which report is newer" and "which lines we have" are two independent questions.
    /// <see cref="IsLaterReport"/> answers only the first, and it decides the fields that are
    /// neither derived from lines nor re-derivable after the merge (warehouse, region,
    /// substatus).
    /// </summary>
    internal static ChannelOrderSnapshot Merge(ChannelOrderSnapshot existing, ChannelOrderSnapshot incoming)
    {
        bool isIncomingLater = IsLaterReport(incoming, existing);

        List<ChannelOrderLineSnapshot> mergedLines = UnionLines(existing.Lines, incoming.Lines);
        ChannelOrderSnapshot headerSource = isIncomingLater ? incoming : existing;

        // The header is reduced from the merged line set, never taken from the winning report:
        // either half may hold the only cancelled unit of a cart, or the only live one, and the
        // winning report's own header describes the units it happened to carry. The placement
        // instant goes through the same reduction, so the merged cart keeps the earliest instant
        // any half reported.
        ChannelOrderHeaderFacts mergedHeader = ChannelOrderHeaderFacts.Reduce(
            mergedLines,
            new[] { existing.OrderDate, incoming.OrderDate });

        return headerSource with
        {
            Lines = mergedLines,
            ExternalStatus = mergedHeader.ExternalStatus,
            CancelReason = mergedHeader.CancelReason,
            OrderDate = mergedHeader.OrderDate,
            StatusChangedAt = mergedHeader.StatusChangedAt,
            ChannelReportedTotal = mergedHeader.ChannelReportedTotal,
        };
    }

    /// <summary>
    /// Unions the lines of two reports of one order, keyed by
    /// <see cref="ChannelOrderLineSnapshot.LineKey"/>.
    /// </summary>
    internal static List<ChannelOrderLineSnapshot> UnionLines(
        List<ChannelOrderLineSnapshot> existing,
        List<ChannelOrderLineSnapshot> incoming)
    {
        List<ChannelOrderLineSnapshot> merged = new List<ChannelOrderLineSnapshot>(existing);

        foreach (ChannelOrderLineSnapshot line in incoming)
        {
            int index = merged.FindIndex(
                existingLine => string.Equals(existingLine.LineKey, line.LineKey, StringComparison.Ordinal));

            if (index < 0)
            {
                merged.Add(line);
                continue;
            }

            if (IsLaterLine(line, merged[index]))
            {
                merged[index] = line;
            }
        }

        return merged;
    }

    /// <summary>
    /// Orders whether <paramref name="candidate"/> is a later version of one line than
    /// <paramref name="current"/>. A version reporting no instant never displaces one that reports
    /// an instant, and equal instants resolve in favour of the accumulated version, so replaying a
    /// report cannot flip the stored facts back and forth.
    /// </summary>
    internal static bool IsLaterLine(ChannelOrderLineSnapshot candidate, ChannelOrderLineSnapshot current)
    {
        DateTimeOffset? candidateInstant = candidate.StatusChangedAt;
        DateTimeOffset? currentInstant = current.StatusChangedAt;

        if (candidateInstant == null)
        {
            return false;
        }

        if (currentInstant == null)
        {
            return true;
        }

        return candidateInstant.Value > currentInstant.Value;
    }

    /// <summary>
    /// Orders whether <paramref name="candidate"/> is a later report of the same order than
    /// <paramref name="current"/>. <c>StatusChangedAt</c> is the channel's own "as of" instant and
    /// decides; <c>OrderDate</c> breaks the tie where a channel reports no change time, and an
    /// exact tie keeps the current report so the merge does not depend on arrival order.
    /// </summary>
    internal static bool IsLaterReport(ChannelOrderSnapshot candidate, ChannelOrderSnapshot current)
    {
        DateTimeOffset candidateInstant = candidate.StatusChangedAt ?? candidate.OrderDate;
        DateTimeOffset currentInstant = current.StatusChangedAt ?? current.OrderDate;

        return candidateInstant > currentInstant;
    }
}
