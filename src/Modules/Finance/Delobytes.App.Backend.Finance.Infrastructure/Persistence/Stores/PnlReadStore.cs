using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Finance.Application.Attribution;
using Delobytes.App.Backend.Finance.Application.Interfaces;
using Delobytes.App.Backend.Finance.Application.Models;
using Delobytes.App.Backend.Finance.Application.ReadModels;
using Delobytes.App.Backend.Finance.Domain.Entities;
using Delobytes.App.Backend.Finance.Domain.Enums;
using Delobytes.App.Backend.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Delobytes.App.Backend.Finance.Infrastructure.Persistence.Stores;

/// <summary>
/// EF Core implementation of <see cref="IPnlReadStore"/>.
/// </summary>
public class PnlReadStore : IPnlReadStore
{
    private static readonly Expression<Func<OrderLineRef, AttributionCandidate>> ToCandidate =
        reference => new AttributionCandidate(
            reference.OrderLineId,
            reference.ChannelId,
            reference.ExternalOrderId,
            reference.ExternalLineId,
            reference.ExternalProductId);

    private readonly FinanceDbContext _context;
    private readonly ITenantContext _tenantContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="PnlReadStore"/> class.
    /// </summary>
    public PnlReadStore(FinanceDbContext context, ITenantContext tenantContext)
    {
        _context = context;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc/>
    public async Task<PnlLinePage> GetLinesAsync(
        string currency,
        PnlLineFilter filter,
        PnlLineSort sort,
        int take,
        PnlLineCursor? after,
        CancellationToken cancellationToken)
    {
        EnsureTenant();

        if (take < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(take), take, "A page must contain at least one row.");
        }

        // One row past the page tells us whether another page exists without a second round trip.
        List<PnlLineRow> rows = await BuildPageQuery(currency, filter, sort, after)
            .Take(take + 1)
            .ToListAsync(cancellationToken);

        bool hasMore = rows.Count > take;

        if (hasMore)
        {
            rows.RemoveRange(take, rows.Count - take);
        }

        return new PnlLinePage
        {
            Rows = rows,
            NextToken = hasMore ? PnlLineCursor.ForRow(rows[rows.Count - 1], sort).ToString() : null,
            ExcludedByCurrency = await BuildFilteredQuery(filter)
                .CountAsync(row => row.Currency != currency, cancellationToken),
        };
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<PnlLineRow> StreamForSummaryAsync(
        string currency,
        PnlLineFilter filter,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Checked before the first row is pulled: a missing tenant must fail the call, not surface
        // halfway through the caller's loop.
        EnsureTenant();

        await foreach (PnlLineRow row in BuildLinesQuery(currency, filter)
            .AsAsyncEnumerable()
            .WithCancellation(cancellationToken))
        {
            yield return row;
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PnlCurrencyExclusion>> GetCurrencyExclusionsAsync(
        string currency,
        PnlLineFilter filter,
        CancellationToken cancellationToken)
    {
        EnsureTenant();

        // One aggregate statement over the same filters the report used: the money the reader cannot
        // see is the money of the very lines a single-currency total left out.
        return await BuildFilteredQuery(filter)
            .Where(row => row.Currency != currency)
            .GroupBy(row => row.Currency)
            .Select(group => new PnlCurrencyExclusion
            {
                Currency = group.Key,
                Lines = group.Count(),
                RevenueAmount = group.Sum(row => row.RevenueAmount ?? 0m),
            })
            .OrderBy(exclusion => exclusion.Currency)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<PnlDiagnosticsResult> GetDiagnosticsAsync(
        string currency,
        Guid? channelId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        // Every figure below is read in the reporting currency only: the diagnostics are compared
        // with a single-currency summary, so counting or summing another currency here would make
        // the two disagree. The other currencies are reported as exclusions at the end.
        IQueryable<PnlEntry> entryQuery = _context.PnlEntries
            .AsNoTracking()
            .Where(e =>
                e.SupersededByEntryId == null
                && e.Currency == currency
                && e.DocumentDate >= from
                && e.DocumentDate <= to);

        if (channelId.HasValue)
        {
            Guid channel = channelId.Value;
            entryQuery = entryQuery.Where(e => e.ChannelId == channel);
        }

        List<PnlEntry> entries = await entryQuery.ToListAsync(cancellationToken);

        List<AttributionCandidate> candidates = await BuildAttributionCandidatesQuery(entries)
            .ToListAsync(cancellationToken);

        PnlEntryAttributionResult attribution = PnlEntryAttribution.Attribute(entries, candidates);

        IQueryable<PnlLine> periodLines = BuildPeriodLines(currency, channelId, from, to);

        List<decimal> differences = await periodLines
            .Where(l => l.ControlDifferenceAmount != null)
            .Select(l => l.ControlDifferenceAmount!.Value)
            .ToListAsync(cancellationToken);

        // Not an attribution gap: these lines are already closed with money, they just carry no
        // catalogue product yet — either Catalog has not resolved the cost or the channel product is
        // not linked. Surfacing the count and the revenue keeps that money visible instead of
        // letting it silently sit outside every per-product view.
        List<decimal?> unlinkedRevenue = await BuildFilteredQuery(
                new PnlLineFilter { ChannelId = channelId, From = from, To = to })
            .Where(row => row.Currency == currency && row.ProductId == null)
            .Select(row => row.RevenueAmount)
            .ToListAsync(cancellationToken);

        // Counted one state at a time: three cheap counts are more predictable than a grouped
        // projection over an enum stored by name.
        int provisionalLines = await periodLines
            .CountAsync(l => l.CompletenessState == PnlLineCompleteness.Provisional, cancellationToken);

        int overdueLines = await periodLines
            .CountAsync(l => l.CompletenessState == PnlLineCompleteness.Overdue, cancellationToken);

        int closedWithExceptionLines = await periodLines
            .CountAsync(l => l.CompletenessState == PnlLineCompleteness.ClosedWithException, cancellationToken);

        // The estimated gap is a size, not a booked amount: it only covers expectations whose
        // product has enough comparable history to be estimated at all.
        IQueryable<PnlLine> soldLines = periodLines.Where(l => l.SaleDate != null);

        List<decimal> estimates = await _context.PnlLineExpectations
            .AsNoTracking()
            .Where(e => e.State == PnlExpectationState.Expected || e.State == PnlExpectationState.Overdue)
            .Where(e => soldLines.Any(l => l.OrderLineId == e.OrderLineId))
            .Select(e => e.AmountEstimate)
            .Where(a => a != null)
            .Select(a => a!.Value)
            .ToListAsync(cancellationToken);

        PnlLineFilter periodFilter = new PnlLineFilter { ChannelId = channelId, From = from, To = to };

        IReadOnlyList<PnlCurrencyExclusion> excludedCurrencies = await GetCurrencyExclusionsAsync(
            currency,
            periodFilter,
            cancellationToken);

        return new PnlDiagnosticsResult
        {
            Currency = currency,
            UnattributedEntries = attribution.UnmatchedEntries.Count,
            AmbiguousAttributions = attribution.AmbiguousAttributions,
            ControlDifference = differences.Sum(),
            LinesWithOpenControlDifference = differences.Count(d => Math.Abs(d) > 0.01m),
            LinesWithoutProductLink = unlinkedRevenue.Count,
            UnlinkedRevenueAmount = unlinkedRevenue.Where(r => r.HasValue).Sum(r => r!.Value),
            ProvisionalLines = provisionalLines,
            OverdueLines = overdueLines,
            ClosedWithExceptionLines = closedWithExceptionLines,
            EstimatedMissingAmount = estimates.Sum(),
            ExcludedByCurrency = excludedCurrencies.Sum(exclusion => exclusion.Lines),
            ExcludedCurrencies = excludedCurrencies.ToList(),
        };
    }

    /// <summary>
    /// Builds the order line facts the period's entries can match. Internal so tests can assert the
    /// narrowing runs as a SQL filter instead of reading every line of the tenant.
    /// </summary>
    internal IQueryable<AttributionCandidate> BuildAttributionCandidatesQuery(IReadOnlyCollection<PnlEntry> entries)
    {
        IQueryable<OrderLineRef> references = _context.OrderLineRefs.AsNoTracking();

        if (entries.Count == 0)
        {
            return references.Where(reference => false).Select(ToCandidate);
        }

        List<Guid> channelIds = entries.Select(entry => entry.ChannelId).Distinct().ToList();

        List<string> externalLineIds = entries
            .Where(entry => !string.IsNullOrEmpty(entry.ExternalLineId))
            .Select(entry => entry.ExternalLineId!)
            .Distinct()
            .ToList();

        List<string> externalOrderIds = entries
            .Where(entry => string.IsNullOrEmpty(entry.ExternalLineId) && !string.IsNullOrEmpty(entry.ExternalOrderId))
            .Select(entry => entry.ExternalOrderId!)
            .Distinct()
            .ToList();

        references = references.Where(reference => channelIds.Contains(reference.ChannelId));

        // The channel and the identity are filtered as two independent sets: a channel/id pair that
        // never co-occurs still survives the filter. That only enlarges the candidate set — the
        // attribution narrows by the exact key in memory, so no entry is matched that should not be.
        if (externalLineIds.Count > 0 && externalOrderIds.Count > 0)
        {
            references = references.Where(reference =>
                externalLineIds.Contains(reference.ExternalLineId!)
                || externalOrderIds.Contains(reference.ExternalOrderId));
        }
        else if (externalLineIds.Count > 0)
        {
            references = references.Where(reference => externalLineIds.Contains(reference.ExternalLineId!));
        }
        else if (externalOrderIds.Count > 0)
        {
            references = references.Where(reference => externalOrderIds.Contains(reference.ExternalOrderId));
        }
        else
        {
            return references.Where(reference => false).Select(ToCandidate);
        }

        return references.Select(ToCandidate);
    }

    /// <summary>
    /// Builds the P&amp;L rows of one currency. Internal so tests can assert the SQL EF Core
    /// produces for the joins and the filters.
    /// </summary>
    internal IQueryable<PnlLineRow> BuildLinesQuery(string currency, PnlLineFilter filter)
    {
        return BuildFilteredQuery(filter).Where(row => row.Currency == currency);
    }

    /// <summary>
    /// Builds one ordered and cursored page of P&amp;L rows. Internal so tests can assert that the
    /// ordering and the keyset predicate run in SQL.
    /// </summary>
    internal IQueryable<PnlLineRow> BuildPageQuery(
        string currency,
        PnlLineFilter filter,
        PnlLineSort sort,
        PnlLineCursor? after)
    {
        return ApplySort(ApplyCursor(BuildLinesQuery(currency, filter), sort, after), sort);
    }

    /// <summary>
    /// Orders the page by the requested key and then by the identifier, which keeps the order total
    /// and therefore the keyset stable when the key value repeats.
    /// </summary>
    /// <remarks>
    /// Rows with an unknown key form one block at the end of the page in both directions. PostgreSQL
    /// would put them first when descending and last when ascending, and a page that opens with
    /// unknown margins hides the figures the reader asked for; the explicit block also makes the
    /// order the same whatever the provider does.
    /// </remarks>
    private static IOrderedQueryable<PnlLineRow> ApplySort(IQueryable<PnlLineRow> query, PnlLineSort sort)
    {
        return sort.Key switch
        {
            PnlLineSortKey.Margin => sort.Descending
                ? query.OrderBy(row => row.MarginAmount == null)
                    .ThenByDescending(row => row.MarginAmount)
                    .ThenBy(row => row.Id)
                : query.OrderBy(row => row.MarginAmount == null)
                    .ThenBy(row => row.MarginAmount)
                    .ThenBy(row => row.Id),
            _ => sort.Descending
                ? query.OrderBy(row => row.PeriodDate == null)
                    .ThenByDescending(row => row.PeriodDate)
                    .ThenBy(row => row.Id)
                : query.OrderBy(row => row.PeriodDate == null)
                    .ThenBy(row => row.PeriodDate)
                    .ThenBy(row => row.Id),
        };
    }

    /// <summary>
    /// Narrows the query to the rows that follow the cursor in the requested order.
    /// </summary>
    /// <remarks>
    /// The key is read at the position of the row the token points at, not in memory: the predicate
    /// has to reach the database, otherwise a page would be cut from the whole tenant. A null key
    /// value is a real position — an unknown margin — and its block comes last in both directions,
    /// so a cursor inside a known value still lets the unknown rows through.
    /// </remarks>
    private static IQueryable<PnlLineRow> ApplyCursor(
        IQueryable<PnlLineRow> query,
        PnlLineSort sort,
        PnlLineCursor? after)
    {
        if (after == null)
        {
            return query;
        }

        Guid id = after.Id;

        if (sort.Key == PnlLineSortKey.Margin)
        {
            decimal? value = after.MarginValue;

            if (value == null)
            {
                return query.Where(row => row.MarginAmount == null && row.Id.CompareTo(id) > 0);
            }

            decimal margin = value.Value;

            return sort.Descending
                ? query.Where(row =>
                    row.MarginAmount == null
                    || row.MarginAmount < margin
                    || (row.MarginAmount == margin && row.Id.CompareTo(id) > 0))
                : query.Where(row =>
                    row.MarginAmount == null
                    || row.MarginAmount > margin
                    || (row.MarginAmount == margin && row.Id.CompareTo(id) > 0));
        }

        DateOnly? date = after.DateValue;

        if (date == null)
        {
            return query.Where(row => row.PeriodDate == null && row.Id.CompareTo(id) > 0);
        }

        DateOnly periodDate = date.Value;

        return sort.Descending
            ? query.Where(row =>
                row.PeriodDate == null
                || row.PeriodDate < periodDate
                || (row.PeriodDate == periodDate && row.Id.CompareTo(id) > 0))
            : query.Where(row =>
                row.PeriodDate == null
                || row.PeriodDate > periodDate
                || (row.PeriodDate == periodDate && row.Id.CompareTo(id) > 0));
    }

    /// <summary>
    /// Builds the lines of one reporting currency and period, optionally narrowed to a channel. The
    /// channel lives on the order line reference, so the narrowing is an EXISTS instead of a join:
    /// the counts keep their shape and a line without a reference is not dropped when no channel is
    /// selected.
    /// </summary>
    private IQueryable<PnlLine> BuildPeriodLines(
        string currency,
        Guid? channelId,
        DateOnly from,
        DateOnly to)
    {
        IQueryable<PnlLine> lines = _context.PnlLines
            .AsNoTracking()
            .Where(l =>
                l.Currency == currency
                && (l.SaleDate ?? l.FinancialPeriodFrom) >= from
                && (l.SaleDate ?? l.FinancialPeriodFrom) <= to);

        if (channelId.HasValue)
        {
            Guid channel = channelId.Value;
            lines = lines.Where(l =>
                _context.OrderLineRefs.Any(r => r.OrderLineId == l.OrderLineId && r.ChannelId == channel));
        }

        return lines;
    }

    /// <summary>Builds the joined and filtered rows without the currency restriction.</summary>
    internal IQueryable<PnlLineRow> BuildFilteredQuery(PnlLineFilter filter)
    {
        return ApplyFilters(BuildProjection(), filter);
    }

    private IQueryable<PnlLineRow> BuildProjection()
    {
        return from line in _context.PnlLines.AsNoTracking()
               join reference in _context.OrderLineRefs.AsNoTracking()
                   on line.OrderLineId equals reference.OrderLineId
               join cost in _context.OrderLineCosts.AsNoTracking()
                   on line.OrderLineId equals cost.OrderLineId into costs
               from cost in costs.DefaultIfEmpty()
               select new PnlLineRow
               {
                   Id = line.Id,
                   OrderLineId = line.OrderLineId,
                   ChannelId = reference.ChannelId,
                   // The product is the one Catalog resolved the cost for. The channel product is a
                   // different identity and must not stand in for it: a substitution would put the
                   // sale into a group no catalogue product can be joined to and would hide it from
                   // the diagnostics that count unlinked lines.
                   ProductId = cost == null ? null : cost.ProductId,
                   ChannelProductId = reference.ChannelProductId,
                   // The ordered units come from the reference the join already reads: no extra
                   // round trip and no second source of the same fact.
                   Quantity = reference.Quantity,
                   ReturnedQuantity = line.ReturnedQuantity,
                   Currency = line.Currency,
                   SaleDate = line.SaleDate,
                   // A line that never sold still has money on it: a non-buyout carries only its
                   // delivery charge. Its sale date is null, so the period the charge belongs to is
                   // the one written from its documents. Without this the line would be invisible to
                   // every from/to filter and to the summary.
                   PeriodDate = line.SaleDate ?? line.FinancialPeriodFrom,
                   State = line.State,
                   CostStatus = cost == null ? null : cost.Status,
                   RevenueAmount = line.RevenueAmount,
                   CommissionAmount = line.CommissionAmount,
                   AcquiringAmount = line.AcquiringAmount,
                   DeliveryFeeAmount = line.DeliveryFeeAmount,
                   RefundAmount = line.RefundAmount,
                   CompensationAmount = line.CompensationAmount,
                   NetRevenueAmount = line.NetRevenueAmount,
                   ControlTotalAmount = line.ControlTotalAmount,
                   ControlDifferenceAmount = line.ControlDifferenceAmount,
                   ProductionCostAmount = line.ProductionCostAmount,
                   ProductionCostIsComplete = line.ProductionCostIsComplete,
                   // The ordering key of the margin sort, written as the same rule
                   // PnlTaxCalculator.LineMargin applies in memory: a line that never sold has no
                   // cost of goods, so its result is the net revenue, while a sold line needs a
                   // complete cost fact and is unknown without it. A margin built on a partial cost
                   // would be an overstated figure, not a conservative one.
                   MarginAmount =
                       line.SaleDate == null || reference.Quantity - line.ReturnedQuantity <= 0
                           ? line.NetRevenueAmount
                           : line.ProductionCostIsComplete == true
                               && line.NetRevenueAmount != null
                               && line.ProductionCostAmount != null
                                   ? line.NetRevenueAmount - line.ProductionCostAmount
                                   : null,
                   // The cost fact describes the ordered units, while the line stores only the cost
                   // of the units that stayed sold. The investment base has to come from the fact
                   // itself, so it is projected here instead of being rebuilt from the line.
                   UnitTotalCost = cost == null ? null : cost.UnitTotalCost,
                   TotalCost = cost == null ? null : cost.TotalCost,
                   CostQuantity = cost == null ? null : cost.Quantity,
                   CostIsComplete = cost == null ? null : cost.IsComplete,
                   CompletenessState = line.CompletenessState,
                   MissingDocumentTypes = line.MissingDocumentTypes,
                   ExpectedBy = line.ExpectedBy,
               };
    }

    private static IQueryable<PnlLineRow> ApplyFilters(IQueryable<PnlLineRow> query, PnlLineFilter filter)
    {
        if (filter.ChannelId.HasValue)
        {
            Guid channelId = filter.ChannelId.Value;
            query = query.Where(row => row.ChannelId == channelId);
        }

        if (filter.ProductId.HasValue)
        {
            Guid productId = filter.ProductId.Value;
            query = query.Where(row => row.ProductId == productId);
        }

        if (filter.ProductIds is { Count: > 0 })
        {
            List<Guid> productIds = filter.ProductIds.ToList();
            query = query.Where(row => row.ProductId.HasValue && productIds.Contains(row.ProductId.Value));
        }

        if (filter.ChannelProductId.HasValue)
        {
            Guid channelProductId = filter.ChannelProductId.Value;
            query = query.Where(row => row.ChannelProductId == channelProductId);
        }

        if (filter.From.HasValue)
        {
            DateOnly from = filter.From.Value;
            query = query.Where(row => row.PeriodDate >= from);
        }

        if (filter.To.HasValue)
        {
            DateOnly to = filter.To.Value;
            query = query.Where(row => row.PeriodDate <= to);
        }

        if (filter.State.HasValue)
        {
            PnlLineState state = filter.State.Value;
            query = query.Where(row => row.State == state);
        }

        if (filter.CompletenessStates is { Count: > 0 })
        {
            List<PnlLineCompleteness> states = filter.CompletenessStates.ToList();
            query = query.Where(row => states.Contains(row.CompletenessState));
        }

        return query;
    }

    private void EnsureTenant()
    {
        if (_tenantContext.TenantId is null)
        {
            throw new InvalidOperationException("Cannot read P&L lines without a resolved tenant.");
        }
    }
}
