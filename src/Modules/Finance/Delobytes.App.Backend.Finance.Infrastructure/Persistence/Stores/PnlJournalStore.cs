using Delobytes.App.Backend.Finance.Application.Interfaces;
using Delobytes.App.Backend.Finance.Application.Models;
using Delobytes.App.Backend.Finance.Domain.Entities;
using Delobytes.App.Backend.Finance.Domain.Enums;
using Delobytes.App.Backend.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Delobytes.App.Backend.Finance.Infrastructure.Persistence.Stores;

/// <summary>
/// EF Core implementation of <see cref="IPnlJournalStore"/>. Every lookup is set-based: a batch of
/// journal entries costs a fixed number of round trips, not one per row.
/// </summary>
public class PnlJournalStore : IPnlJournalStore
{
    private readonly FinanceDbContext _context;

    /// <summary>
    /// Initializes a new instance of the <see cref="PnlJournalStore"/> class.
    /// </summary>
    /// <param name="context">Finance database context.</param>
    public PnlJournalStore(FinanceDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PnlEntry>> LoadByIdentityAsync(
        IReadOnlyCollection<JournalIdentityKey> keys,
        CancellationToken cancellationToken)
    {
        if (keys.Count == 0)
        {
            return Array.Empty<PnlEntry>();
        }

        List<string> reportInstanceKeys = keys.Select(k => k.ReportInstanceKey).Distinct().ToList();
        List<string> externalEntryIds = keys.Select(k => k.ExternalEntryId).Distinct().ToList();

        List<PnlEntry> candidates = await _context.PnlEntries
            .AsNoTracking()
            .Where(e => reportInstanceKeys.Contains(e.ReportInstanceKey) && externalEntryIds.Contains(e.ExternalEntryId))
            .ToListAsync(cancellationToken);

        HashSet<JournalIdentityKey> lookup = keys.ToHashSet();

        return candidates
            .Where(e => lookup.Contains(new JournalIdentityKey(
                e.Source,
                e.ReportInstanceKey,
                e.ExternalEntryId,
                e.ExternalProductId,
                e.Kind,
                e.Type)))
            .ToList();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PnlEntry>> LoadCurrentByComparisonAsync(
        IReadOnlyCollection<JournalComparisonKey> keys,
        CancellationToken cancellationToken)
    {
        if (keys.Count == 0)
        {
            return Array.Empty<PnlEntry>();
        }

        List<string> externalEntryIds = keys.Select(k => k.ExternalEntryId).Distinct().ToList();
        List<string> productIds = keys.Select(k => k.ExternalProductId).Distinct().ToList();

        List<PnlEntry> candidates = await _context.PnlEntries
            .AsNoTracking()
            .Where(e =>
                e.SupersededByEntryId == null
                && externalEntryIds.Contains(e.ExternalEntryId)
                && productIds.Contains(e.ExternalProductId))
            .ToListAsync(cancellationToken);

        HashSet<JournalComparisonKey> lookup = keys.ToHashSet();

        return candidates
            .Where(e => lookup.Contains(new JournalComparisonKey(
                e.Source,
                e.ExternalEntryId,
                e.ExternalProductId,
                e.Kind,
                e.Type)))
            .ToList();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PnlEntry>> LoadCurrentByAttributionAsync(
        IReadOnlyCollection<OrderLineAttributionKey> keys,
        CancellationToken cancellationToken)
    {
        if (keys.Count == 0)
        {
            return Array.Empty<PnlEntry>();
        }

        List<PnlEntry> result = new();

        List<OrderLineAttributionKey> lineKeys = keys.Where(k => k.ExternalLineId != null).ToList();

        if (lineKeys.Count > 0)
        {
            List<Guid> channelIds = lineKeys.Select(k => k.ChannelId).Distinct().ToList();
            List<string> externalLineIds = lineKeys.Select(k => k.ExternalLineId!).Distinct().ToList();

            List<PnlEntry> lineEntries = await _context.PnlEntries
                .AsNoTracking()
                .Where(e =>
                    e.SupersededByEntryId == null
                    && e.ExternalLineId != null
                    && channelIds.Contains(e.ChannelId)
                    && externalLineIds.Contains(e.ExternalLineId))
                .ToListAsync(cancellationToken);

            HashSet<OrderLineAttributionKey> lookup = lineKeys.ToHashSet();

            result.AddRange(lineEntries.Where(e => lookup.Contains(
                OrderLineAttributionKey.ForLine(e.ChannelId, e.ExternalLineId!))));
        }

        List<OrderLineAttributionKey> orderKeys = keys.Where(k => k.ExternalLineId == null).ToList();

        if (orderKeys.Count > 0)
        {
            List<Guid> channelIds = orderKeys.Select(k => k.ChannelId).Distinct().ToList();
            List<string> externalOrderIds = orderKeys.Select(k => k.ExternalOrderId!).Distinct().ToList();
            List<string> productIds = orderKeys.Select(k => k.ExternalProductId!).Distinct().ToList();

            List<PnlEntry> orderEntries = await _context.PnlEntries
                .AsNoTracking()
                .Where(e =>
                    e.SupersededByEntryId == null
                    && e.ExternalOrderId != null
                    && channelIds.Contains(e.ChannelId)
                    && externalOrderIds.Contains(e.ExternalOrderId)
                    && productIds.Contains(e.ExternalProductId))
                .ToListAsync(cancellationToken);

            HashSet<OrderLineAttributionKey> lookup = orderKeys.ToHashSet();

            result.AddRange(orderEntries.Where(e => lookup.Contains(
                OrderLineAttributionKey.ForOrder(e.ChannelId, e.ExternalOrderId!, e.ExternalProductId))));
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyCollection<string>> LoadInstanceKeysAsync(
        Guid channelId,
        PnlSource source,
        string reportId,
        CancellationToken cancellationToken)
    {
        string prefix = reportId + ":";

        List<string> keys = await _context.PnlEntries
            .AsNoTracking()
            .Where(e =>
                e.ChannelId == channelId
                && e.Source == source
                && e.ReportInstanceKey.StartsWith(prefix))
            .Select(e => e.ReportInstanceKey)
            .Distinct()
            .ToListAsync(cancellationToken);

        return keys;
    }

    /// <inheritdoc/>
    public async Task AppendAsync(IReadOnlyCollection<PnlEntry> entries, CancellationToken cancellationToken)
    {
        if (entries.Count == 0)
        {
            return;
        }

        _context.PnlEntries.AddRange(entries);

        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task SupersedeAsync(
        IReadOnlyCollection<SupersedeInstruction> instructions,
        CancellationToken cancellationToken)
    {
        if (instructions.Count == 0)
        {
            return;
        }

        foreach (IGrouping<Guid, SupersedeInstruction> group in instructions.GroupBy(i => i.SupersedingEntryId))
        {
            List<Guid> supersededIds = group.Select(i => i.SupersededEntryId).ToList();
            DateTimeOffset supersededAt = group.Min(i => i.SupersededAt);
            Guid supersedingId = group.Key;

            await _context.PnlEntries
                .Where(e => supersededIds.Contains(e.Id) && e.SupersededByEntryId == null)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(e => e.SupersededByEntryId, supersedingId)
                        .SetProperty(e => e.SupersededAt, supersededAt),
                    cancellationToken);
        }
    }
}
