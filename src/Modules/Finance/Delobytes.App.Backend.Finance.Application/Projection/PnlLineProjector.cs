using Delobytes.App.Backend.Contracts.Accounting;
using Delobytes.App.Backend.Finance.Application.Attribution;
using Delobytes.App.Backend.Finance.Application.Interfaces;
using Delobytes.App.Backend.Finance.Application.Models;
using Delobytes.App.Backend.Finance.Domain.Calculation;
using Delobytes.App.Backend.Finance.Domain.Entities;
using Delobytes.App.Backend.Finance.Domain.Enums;

namespace Delobytes.App.Backend.Finance.Application.Projection;

/// <summary>
/// Rebuilds the <see cref="PnlLine"/> projection from the journal and the cost fact. The line is
/// derived, never entered by hand: any change to either input goes through here, and a full rebuild
/// produces the same values.
/// </summary>
public class PnlLineProjector
{
    private readonly IPnlJournalStore _journal;
    private readonly IOrderLineRefStore _orderLineRefs;
    private readonly IOrderLineCostStore _orderLineCosts;
    private readonly IPnlLineStore _lines;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="PnlLineProjector"/> class.
    /// </summary>
    /// <param name="journal">Journal persistence port.</param>
    /// <param name="orderLineRefs">Order line projection port.</param>
    /// <param name="orderLineCosts">Production cost port.</param>
    /// <param name="lines">P&amp;L line persistence port.</param>
    /// <param name="timeProvider">Clock source.</param>
    public PnlLineProjector(
        IPnlJournalStore journal,
        IOrderLineRefStore orderLineRefs,
        IOrderLineCostStore orderLineCosts,
        IPnlLineStore lines,
        TimeProvider timeProvider)
    {
        _journal = journal;
        _orderLineRefs = orderLineRefs;
        _orderLineCosts = orderLineCosts;
        _lines = lines;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Recomputes the lines of <paramref name="orderLineIds"/>.
    /// </summary>
    /// <param name="orderLineIds">Order lines to recompute.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task RecomputeAsync(
        IReadOnlyCollection<Guid> orderLineIds,
        CancellationToken cancellationToken)
    {
        if (orderLineIds.Count == 0)
        {
            return;
        }

        IReadOnlyList<OrderLineRef> refs = await _orderLineRefs.LoadAsync(orderLineIds, cancellationToken);

        await RecomputeCoreAsync(refs, cancellationToken);
    }

    /// <summary>
    /// Resolves the order lines behind <paramref name="keys"/> in one query and recomputes them.
    /// </summary>
    /// <param name="keys">Attribution keys affected by a journal append.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task RecomputeForKeysAsync(
        IReadOnlyCollection<OrderLineAttributionKey> keys,
        CancellationToken cancellationToken)
    {
        if (keys.Count == 0)
        {
            return;
        }

        IReadOnlyList<OrderLineRef> refs = await _orderLineRefs.LoadByAttributionAsync(keys, cancellationToken);

        await RecomputeCoreAsync(refs, cancellationToken);
    }

    private async Task RecomputeCoreAsync(
        IReadOnlyList<OrderLineRef> refs,
        CancellationToken cancellationToken)
    {
        if (refs.Count == 0)
        {
            return;
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();

        List<OrderLineAttributionKey> keys = refs.Select(AttributionKeyOf).Distinct().ToList();

        IReadOnlyList<PnlEntry> entries = await _journal.LoadCurrentByAttributionAsync(keys, cancellationToken);

        PnlEntryAttributionResult attribution = PnlEntryAttribution.Attribute(entries, refs);

        Dictionary<Guid, List<PnlEntry>> entriesByLine = new();

        foreach (PnlEntryAttributionMatch match in attribution.Matches)
        {
            if (!entriesByLine.TryGetValue(match.OrderLineId, out List<PnlEntry>? bucket))
            {
                bucket = new List<PnlEntry>();
                entriesByLine.Add(match.OrderLineId, bucket);
            }

            bucket.Add(match.Entry);
        }

        List<Guid> lineIds = refs.Select(r => r.OrderLineId).ToList();

        IReadOnlyList<OrderLineCost> costs = await _orderLineCosts.LoadAsync(lineIds, cancellationToken);
        Dictionary<Guid, OrderLineCost> costsByLine = costs.ToDictionary(c => c.OrderLineId);

        IReadOnlyList<PnlLine> existingLines = await _lines.LoadAsync(lineIds, cancellationToken);
        Dictionary<Guid, PnlLine> linesByOrderLine = existingLines.ToDictionary(l => l.OrderLineId);

        foreach (OrderLineRef orderLineRef in refs)
        {
            PnlLineTotals totals = PnlLineAggregator.Calculate(
                entriesByLine.TryGetValue(orderLineRef.OrderLineId, out List<PnlEntry>? lineEntries)
                    ? lineEntries
                    : Array.Empty<PnlEntry>());

            if (!linesByOrderLine.TryGetValue(orderLineRef.OrderLineId, out PnlLine? line))
            {
                line = new PnlLine
                {
                    Id = Guid.NewGuid(),
                    OrderLineId = orderLineRef.OrderLineId,
                    CreatedAt = now,
                };

                _lines.Add(line);
            }

            ApplyTotals(line, totals, orderLineRef, now);

            ApplyCost(
                line,
                totals,
                costsByLine.TryGetValue(orderLineRef.OrderLineId, out OrderLineCost? cost) ? cost : null);
        }

        await _lines.SaveChangesAsync(cancellationToken);
    }

    private static void ApplyTotals(
        PnlLine line,
        PnlLineTotals totals,
        OrderLineRef orderLineRef,
        DateTimeOffset now)
    {
        line.RevenueAmount = totals.RevenueAmount;
        line.RevenueSource = SourceOf(totals.RevenueAmount);

        line.CommissionAmount = totals.CommissionAmount;
        line.CommissionSource = SourceOf(totals.CommissionAmount);

        line.AcquiringAmount = totals.AcquiringAmount;
        line.AcquiringSource = SourceOf(totals.AcquiringAmount);

        line.DeliveryFeeAmount = totals.DeliveryFeeAmount;
        line.DeliveryFeeSource = SourceOf(totals.DeliveryFeeAmount);

        line.RefundAmount = totals.RefundAmount;
        line.RefundSource = SourceOf(totals.RefundAmount);

        line.CompensationAmount = totals.CompensationAmount;
        line.CompensationSource = SourceOf(totals.CompensationAmount);

        line.NetRevenueAmount = totals.NetRevenueAmount;
        line.NetRevenueSource = ValueSource.Derived;

        line.ControlTotalAmount = totals.ControlTotalAmount;
        line.ControlDifferenceAmount = totals.ControlDifferenceAmount;

        line.EntryCount = totals.EntryCount;
        line.State = totals.EntryCount > 0 ? PnlLineState.Calculated : PnlLineState.NotCalculated;
        line.SaleDate = totals.SaleDate;
        line.Currency = orderLineRef.Currency;
        line.ComputedAt = now;
        line.UpdatedAt = now;
    }

    private static void ApplyCost(PnlLine line, PnlLineTotals totals, OrderLineCost? cost)
    {
        // Cost of goods applies only to a line that actually sold: a cancellation or a non-buyout
        // keeps its negative result without COGS, because no unit left the seller.
        bool applies = totals.HasSale && cost != null && cost.Status == OrderLineCostStatus.Resolved;

        line.ProductionCostAmount = applies ? cost!.TotalCost : null;
        line.ProductionCostIsComplete = applies ? cost!.IsComplete : null;
    }

    private static ValueSource SourceOf(decimal? amount)
    {
        return amount.HasValue ? ValueSource.ChannelFinanceReport : ValueSource.Unknown;
    }

    private static OrderLineAttributionKey AttributionKeyOf(OrderLineRef orderLineRef)
    {
        if (!string.IsNullOrEmpty(orderLineRef.ExternalLineId))
        {
            return OrderLineAttributionKey.ForLine(orderLineRef.ChannelId, orderLineRef.ExternalLineId);
        }

        return OrderLineAttributionKey.ForOrder(
            orderLineRef.ChannelId,
            orderLineRef.ExternalOrderId,
            orderLineRef.ExternalProductId);
    }
}
