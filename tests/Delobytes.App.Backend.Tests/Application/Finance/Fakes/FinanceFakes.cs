using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Finance.Application.Interfaces;
using Delobytes.App.Backend.Finance.Application.Models;
using Delobytes.App.Backend.Finance.Application.ReadModels;
using Delobytes.App.Backend.Finance.Domain.Entities;
using Delobytes.App.Backend.Finance.Domain.Enums;

namespace Delobytes.App.Backend.Tests.Application.Finance.Fakes;

internal sealed class FixedTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _now;

    public FixedTimeProvider(DateTimeOffset now)
    {
        _now = now;
    }

    public override DateTimeOffset GetUtcNow()
    {
        return _now;
    }
}

internal sealed class InMemoryPnlJournalStore : IPnlJournalStore
{
    public List<PnlEntry> Entries { get; } = new List<PnlEntry>();

    public Task<IReadOnlyList<PnlEntry>> LoadByIdentityAsync(
        IReadOnlyCollection<JournalIdentityKey> keys,
        CancellationToken cancellationToken)
    {
        HashSet<JournalIdentityKey> lookup = keys.ToHashSet();

        IReadOnlyList<PnlEntry> result = Entries
            .Where(e => lookup.Contains(IdentityKeyOf(e)))
            .ToList();

        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<PnlEntry>> LoadCurrentByComparisonAsync(
        IReadOnlyCollection<JournalComparisonKey> keys,
        CancellationToken cancellationToken)
    {
        HashSet<JournalComparisonKey> lookup = keys.ToHashSet();

        IReadOnlyList<PnlEntry> result = Entries
            .Where(e => e.SupersededByEntryId == null && lookup.Contains(ComparisonKeyOf(e)))
            .ToList();

        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<PnlEntry>> LoadCurrentByAttributionAsync(
        IReadOnlyCollection<OrderLineAttributionKey> keys,
        CancellationToken cancellationToken)
    {
        HashSet<OrderLineAttributionKey> lineKeys = keys.Where(k => k.ExternalLineId != null).ToHashSet();
        HashSet<OrderLineAttributionKey> orderKeys = keys.Where(k => k.ExternalLineId == null).ToHashSet();

        IReadOnlyList<PnlEntry> result = Entries
            .Where(e => e.SupersededByEntryId == null)
            .Where(e =>
                (e.ExternalLineId != null
                    && lineKeys.Contains(OrderLineAttributionKey.ForLine(e.ChannelId, e.ExternalLineId)))
                || (e.ExternalOrderId != null
                    && orderKeys.Contains(OrderLineAttributionKey.ForOrder(e.ChannelId, e.ExternalOrderId, e.ExternalProductId))))
            .ToList();

        return Task.FromResult(result);
    }

    public Task<IReadOnlyCollection<string>> LoadInstanceKeysAsync(
        Guid channelId,
        PnlSource source,
        string reportId,
        CancellationToken cancellationToken)
    {
        string prefix = reportId + ":";

        IReadOnlyCollection<string> result = Entries
            .Where(e =>
                e.ChannelId == channelId
                && e.Source == source
                && e.ReportInstanceKey.StartsWith(prefix, StringComparison.Ordinal))
            .Select(e => e.ReportInstanceKey)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return Task.FromResult(result);
    }

    public Task AppendAsync(IReadOnlyCollection<PnlEntry> entries, CancellationToken cancellationToken)
    {
        Entries.AddRange(entries);
        return Task.CompletedTask;
    }

    public Task SupersedeAsync(
        IReadOnlyCollection<SupersedeInstruction> instructions,
        CancellationToken cancellationToken)
    {
        foreach (SupersedeInstruction instruction in instructions)
        {
            PnlEntry? entry = Entries.FirstOrDefault(e => e.Id == instruction.SupersededEntryId);

            if (entry != null)
            {
                entry.SupersededByEntryId = instruction.SupersedingEntryId;
                entry.SupersededAt = instruction.SupersededAt;
            }
        }

        return Task.CompletedTask;
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
}

internal sealed class InMemoryOrderLineRefStore : IOrderLineRefStore
{
    public Dictionary<Guid, OrderLineRef> Refs { get; } = new Dictionary<Guid, OrderLineRef>();

    public Task<IReadOnlyList<OrderLineRef>> LoadAsync(
        IReadOnlyCollection<Guid> orderLineIds,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<OrderLineRef> result = Refs.Values
            .Where(r => orderLineIds.Contains(r.OrderLineId))
            .ToList();

        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<OrderLineRef>> LoadByAttributionAsync(
        IReadOnlyCollection<OrderLineAttributionKey> keys,
        CancellationToken cancellationToken)
    {
        HashSet<OrderLineAttributionKey> lineKeys = keys.Where(k => k.ExternalLineId != null).ToHashSet();
        HashSet<OrderLineAttributionKey> orderKeys = keys.Where(k => k.ExternalLineId == null).ToHashSet();

        IReadOnlyList<OrderLineRef> result = Refs.Values
            .Where(r =>
                (!string.IsNullOrEmpty(r.ExternalLineId)
                    && lineKeys.Contains(OrderLineAttributionKey.ForLine(r.ChannelId, r.ExternalLineId)))
                || orderKeys.Contains(OrderLineAttributionKey.ForOrder(r.ChannelId, r.ExternalOrderId, r.ExternalProductId)))
            .ToList();

        return Task.FromResult(result);
    }

    public Task UpsertAsync(IReadOnlyCollection<OrderLineRef> refs, CancellationToken cancellationToken)
    {
        foreach (OrderLineRef orderLineRef in refs)
        {
            Refs[orderLineRef.OrderLineId] = orderLineRef;
        }

        return Task.CompletedTask;
    }
}

internal sealed class InMemoryOrderLineCostStore : IOrderLineCostStore
{
    public Dictionary<Guid, OrderLineCost> Costs { get; } = new Dictionary<Guid, OrderLineCost>();

    public Task<IReadOnlyList<OrderLineCost>> LoadAsync(
        IReadOnlyCollection<Guid> orderLineIds,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<OrderLineCost> result = Costs.Values
            .Where(c => orderLineIds.Contains(c.OrderLineId))
            .ToList();

        return Task.FromResult(result);
    }

    public Task<bool> TryUpsertNewerAsync(OrderLineCost cost, CancellationToken cancellationToken)
    {
        if (Costs.TryGetValue(cost.OrderLineId, out OrderLineCost? existing)
            && existing.ResolvedAt >= cost.ResolvedAt)
        {
            return Task.FromResult(false);
        }

        Costs[cost.OrderLineId] = cost;

        return Task.FromResult(true);
    }
}

internal sealed class InMemoryPnlLineStore : IPnlLineStore
{
    public Dictionary<Guid, PnlLine> Lines { get; } = new Dictionary<Guid, PnlLine>();

    public Task<IReadOnlyList<PnlLine>> LoadAsync(
        IReadOnlyCollection<Guid> orderLineIds,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<PnlLine> result = Lines.Values
            .Where(l => orderLineIds.Contains(l.OrderLineId))
            .ToList();

        return Task.FromResult(result);
    }

    public void Add(PnlLine line)
    {
        Lines[line.OrderLineId] = line;
    }

    public Task<IReadOnlyList<Guid>> LoadOrderLineIdsAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> result = Lines.Values
            .Where(l => l.SaleDate.HasValue && l.SaleDate.Value >= from && l.SaleDate.Value <= to)
            .Select(l => l.OrderLineId)
            .ToList();

        return Task.FromResult(result);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}



internal sealed class InMemoryPnlExpectationStore : IPnlExpectationStore
{
    public List<PnlLineExpectation> Expectations { get; } = new List<PnlLineExpectation>();

    /// <summary>Estimates the aging pass may resolve, keyed by order line identifier.</summary>
    public Dictionary<Guid, decimal> Estimates { get; } = new Dictionary<Guid, decimal>();

    public Task<IReadOnlyList<PnlLineExpectation>> LoadByOrderLineIdsAsync(
        IReadOnlyCollection<Guid> orderLineIds,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<PnlLineExpectation> result = Expectations
            .Where(e => orderLineIds.Contains(e.OrderLineId))
            .ToList();

        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<PnlLineExpectation>> LoadDueAsync(
        DateTimeOffset now,
        int take,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<PnlLineExpectation> result = Expectations
            .Where(e => e.State == PnlExpectationState.Expected && e.ExpectedBy <= now)
            .OrderBy(e => e.ExpectedBy)
            .Take(take)
            .ToList();

        return Task.FromResult(result);
    }

    public Task<IReadOnlyDictionary<Guid, decimal>> EstimateLogisticsImpactAsync(
        IReadOnlyCollection<Guid> orderLineIds,
        int lookbackDays,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<Guid, decimal> result = orderLineIds
            .Where(id => Estimates.ContainsKey(id))
            .ToDictionary(id => id, id => Estimates[id]);

        return Task.FromResult(result);
    }

    public Task<PnlExpectationPage> GetPageAsync(
        PnlExpectationFilter filter,
        int take,
        string? afterToken,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    public Task<PnlExpectationCloseOutcome> CloseWithExceptionAsync(
        Guid expectationId,
        string reason,
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        PnlLineExpectation? expectation = Expectations.FirstOrDefault(e => e.Id == expectationId);

        if (expectation == null)
        {
            return Task.FromResult(new PnlExpectationCloseOutcome(PnlExpectationCloseStatus.NotFound, null));
        }

        if (expectation.State != PnlExpectationState.Overdue)
        {
            return Task.FromResult(new PnlExpectationCloseOutcome(
                PnlExpectationCloseStatus.WrongState, expectation.OrderLineId));
        }

        expectation.State = PnlExpectationState.ClosedWithException;
        expectation.ClosedAt = now;
        expectation.ClosedByUserId = userId;
        expectation.ClosureReason = reason;
        expectation.UpdatedAt = now;

        return Task.FromResult(new PnlExpectationCloseOutcome(
            PnlExpectationCloseStatus.Applied, expectation.OrderLineId));
    }

    public Task<PnlExpectationCloseOutcome> ReopenAsync(
        Guid expectationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        PnlLineExpectation? expectation = Expectations.FirstOrDefault(e => e.Id == expectationId);

        if (expectation == null)
        {
            return Task.FromResult(new PnlExpectationCloseOutcome(PnlExpectationCloseStatus.NotFound, null));
        }

        if (expectation.State != PnlExpectationState.ClosedWithException)
        {
            return Task.FromResult(new PnlExpectationCloseOutcome(
                PnlExpectationCloseStatus.WrongState, expectation.OrderLineId));
        }

        expectation.State = PnlExpectationState.Overdue;
        expectation.ClosedAt = null;
        expectation.ClosedByUserId = null;
        expectation.ClosureReason = null;
        expectation.UpdatedAt = now;

        return Task.FromResult(new PnlExpectationCloseOutcome(
            PnlExpectationCloseStatus.Applied, expectation.OrderLineId));
    }

    public void Add(PnlLineExpectation expectation)
    {
        Expectations.Add(expectation);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Guid>> LoadTenantIdsWithDueAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> result = Expectations
            .Where(e => e.State == PnlExpectationState.Expected && e.ExpectedBy <= now)
            .Select(e => e.OrderLineId)
            .Distinct()
            .ToList();

        return Task.FromResult(result);
    }
}

internal sealed class StubTenantContext : Delobytes.App.Backend.Contracts.Interfaces.ITenantContext
{
    public Guid? TenantId { get; init; } = Guid.Parse("22222222-2222-2222-2222-222222222222");
}

internal sealed class RecordingEventPublisher : Delobytes.App.Backend.Finance.Application.Interfaces.IEventPublisher
{
    public List<object> Events { get; } = new List<object>();

    public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken)
        where TEvent : class
    {
        Events.Add(@event);
        return Task.CompletedTask;
    }
}
