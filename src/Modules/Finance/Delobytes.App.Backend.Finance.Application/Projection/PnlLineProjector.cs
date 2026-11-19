using Delobytes.App.Backend.Contracts.Accounting;
using Delobytes.App.Backend.Finance.Application.Attribution;
using Delobytes.App.Backend.Finance.Application.Expectation;
using Delobytes.App.Backend.Finance.Application.Interfaces;
using Delobytes.App.Backend.Finance.Application.Models;
using Delobytes.App.Backend.Finance.Domain.Calculation;
using Delobytes.App.Backend.Finance.Domain.Entities;
using Delobytes.App.Backend.Finance.Domain.Enums;
using Microsoft.Extensions.Logging;

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
    private readonly IPnlExpectationStore _expectations;
    private readonly IPnlLineStore _lines;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PnlLineProjector> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PnlLineProjector"/> class.
    /// </summary>
    /// <param name="journal">Journal persistence port.</param>
    /// <param name="orderLineRefs">Order line projection port.</param>
    /// <param name="orderLineCosts">Production cost port.</param>
    /// <param name="expectations">Expected-document open items.</param>
    /// <param name="lines">P&amp;L line persistence port.</param>
    /// <param name="timeProvider">Clock source.</param>
    /// <param name="logger">Logger instance.</param>
    public PnlLineProjector(
        IPnlJournalStore journal,
        IOrderLineRefStore orderLineRefs,
        IOrderLineCostStore orderLineCosts,
        IPnlExpectationStore expectations,
        IPnlLineStore lines,
        TimeProvider timeProvider,
        ILogger<PnlLineProjector> logger)
    {
        _journal = journal;
        _orderLineRefs = orderLineRefs;
        _orderLineCosts = orderLineCosts;
        _expectations = expectations;
        _lines = lines;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Recomputes the lines of <paramref name="orderLineIds"/>. A line whose calculation fails is
    /// skipped rather than thrown; see <see cref="RecomputeCoreAsync"/>.
    /// </summary>
    /// <param name="orderLineIds">Order lines to recompute.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The counters of the pass.</returns>
    public async Task<PnlRecomputeResult> RecomputeAsync(
        IReadOnlyCollection<Guid> orderLineIds,
        CancellationToken cancellationToken)
    {
        if (orderLineIds.Count == 0)
        {
            return new PnlRecomputeResult(0, 0, Array.Empty<Guid>());
        }

        IReadOnlyList<OrderLineRef> refs = await _orderLineRefs.LoadAsync(orderLineIds, cancellationToken);

        return await RecomputeCoreAsync(refs, cancellationToken);
    }

    /// <summary>
    /// Resolves the order lines behind <paramref name="keys"/> in one query and recomputes them.
    /// </summary>
    /// <param name="keys">Attribution keys affected by a journal append.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The counters of the pass.</returns>
    public async Task<PnlRecomputeResult> RecomputeForKeysAsync(
        IReadOnlyCollection<OrderLineAttributionKey> keys,
        CancellationToken cancellationToken)
    {
        if (keys.Count == 0)
        {
            return new PnlRecomputeResult(0, 0, Array.Empty<Guid>());
        }

        IReadOnlyList<OrderLineRef> refs = await _orderLineRefs.LoadByAttributionAsync(keys, cancellationToken);

        return await RecomputeCoreAsync(refs, cancellationToken);
    }

    /// <summary>
    /// Recomputes every line of <paramref name="refs"/>. Each line is calculated and applied inside
    /// its own try/catch, so a bad journal entry on one order line cannot abort the rest of the
    /// batch; the single <see cref="IPnlLineStore.SaveChangesAsync"/> call at the end only ever sees
    /// lines whose calculation finished without throwing.
    /// </summary>
    private async Task<PnlRecomputeResult> RecomputeCoreAsync(
        IReadOnlyList<OrderLineRef> refs,
        CancellationToken cancellationToken)
    {
        if (refs.Count == 0)
        {
            return new PnlRecomputeResult(0, 0, Array.Empty<Guid>());
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

        IReadOnlyList<PnlLineExpectation> existingExpectations =
            await _expectations.LoadByOrderLineIdsAsync(lineIds, cancellationToken);

        Dictionary<Guid, List<PnlLineExpectation>> expectationsByLine = existingExpectations
            .GroupBy(e => e.OrderLineId)
            .ToDictionary(g => g.Key, g => g.ToList());

        int succeeded = 0;
        List<Guid> failedOrderLineIds = new();

        foreach (OrderLineRef orderLineRef in refs)
        {
            try
            {
                List<PnlEntry> lineEntries = entriesByLine.TryGetValue(orderLineRef.OrderLineId, out List<PnlEntry>? bucket)
                    ? bucket
                    : new List<PnlEntry>();

                PnlLineTotals totals = PnlLineAggregator.Calculate(lineEntries);

                bool isNewLine = !linesByOrderLine.TryGetValue(orderLineRef.OrderLineId, out PnlLine? line);

                line ??= new PnlLine
                {
                    Id = Guid.NewGuid(),
                    OrderLineId = orderLineRef.OrderLineId,
                    CreatedAt = now,
                };

                ApplyTotals(line, totals, orderLineRef, now);

                ApplyCost(
                    line,
                    totals,
                    costsByLine.TryGetValue(orderLineRef.OrderLineId, out OrderLineCost? cost) ? cost : null);

                PnlLineExpectation? createdExpectation = ApplyExpectations(
                    line,
                    totals,
                    lineEntries,
                    expectationsByLine.TryGetValue(orderLineRef.OrderLineId, out List<PnlLineExpectation>? lineExpectations)
                        ? lineExpectations
                        : new List<PnlLineExpectation>(),
                    orderLineRef.Currency,
                    now);

                // The line is staged only once every calculation on it succeeded: staging it earlier
                // would let a later throw save a half-applied line with default totals.
                if (isNewLine)
                {
                    _lines.Add(line);
                }

                if (createdExpectation != null)
                {
                    _expectations.Add(createdExpectation);
                }

                succeeded++;
            }
            catch (Exception ex)
            {
                failedOrderLineIds.Add(orderLineRef.OrderLineId);

                // Catches broadly on purpose: one bad order line must not abort the rest of the
                // batch. The line's stored result, if any, is left exactly as it was before this pass.
                _logger.LogError(
                    ex,
                    "Failed to recompute P&L line. OrderLineId={OrderLineId}",
                    orderLineRef.OrderLineId);
            }
        }

        await _lines.SaveChangesAsync(cancellationToken);

        return new PnlRecomputeResult(succeeded, failedOrderLineIds.Count, failedOrderLineIds);
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

    /// <summary>
    /// Maintains the expected-document open items of one line and writes the resulting completeness
    /// onto it. A Wildberries sale always produces a separate delivery row, so a sale without one is
    /// an open expectation until the document arrives or the window elapses.
    /// </summary>
    /// <returns>The expectation created by this pass, or null when none was needed.</returns>
    private static PnlLineExpectation? ApplyExpectations(
        PnlLine line,
        PnlLineTotals totals,
        IReadOnlyCollection<PnlEntry> entries,
        List<PnlLineExpectation> existing,
        string currency,
        DateTimeOffset now)
    {
        bool expectsLogistics = totals.HasSale
            && entries.Any(entry => entry.Source == PnlSource.WbReport)
            && !totals.DeliveryFeeAmount.HasValue;

        PnlLineExpectation? expectation = existing
            .FirstOrDefault(e => e.ExpectedType == PnlEntryType.Logistics);

        // The delivery row arrived, or the sale it belonged to is gone: either way the expectation
        // is resolved by the document rather than by a decision.
        bool closeByDocument = expectation != null
            && !expectsLogistics
            && ExpectationPolicy.IsOpen(expectation.State);

        PnlLineExpectation? created = null;

        if (expectsLogistics && expectation == null && totals.ReportPeriodTo.HasValue)
        {
            created = new PnlLineExpectation
            {
                Id = Guid.NewGuid(),
                OrderLineId = line.OrderLineId,
                ExpectedType = PnlEntryType.Logistics,
                Source = PnlSource.WbReport,
                ExpectedBy = ExpectationPolicy.DeadlineOf(totals.ReportPeriodTo.Value),
                State = PnlExpectationState.Expected,
                Currency = currency,
                CreatedAt = now,
                UpdatedAt = now,
            };
        }

        // Completeness is resolved from the states this pass intends to reach, so no entity is
        // mutated before the whole line has been calculated. A throw anywhere above leaves the
        // stored expectation exactly as it was, the same way it leaves the line untouched.
        List<PnlLineCompletenessEntry> projected = existing
            .Select(e => new PnlLineCompletenessEntry(
                closeByDocument && ReferenceEquals(e, expectation)
                    ? PnlExpectationState.ClosedByDocument
                    : e.State,
                e.ExpectedType,
                e.ExpectedBy))
            .ToList();

        if (created != null)
        {
            projected.Add(new PnlLineCompletenessEntry(
                created.State, created.ExpectedType, created.ExpectedBy));
        }

        PnlLineCompletenessResult completeness = PnlLineCompletenessResolver.Resolve(projected);

        line.CompletenessState = completeness.State;
        line.MissingDocumentTypes = completeness.MissingDocumentTypes.ToList();
        line.ExpectedBy = completeness.ExpectedBy;

        if (closeByDocument)
        {
            expectation!.State = PnlExpectationState.ClosedByDocument;
            expectation.ClosedAt = now;
            expectation.UpdatedAt = now;
        }

        return created;
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
