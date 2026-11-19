using System.Linq.Expressions;
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
        int take,
        string? afterToken,
        CancellationToken cancellationToken)
    {
        EnsureTenant();

        if (take < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(take), take, "A page must contain at least one row.");
        }

        Guid? afterId = ParseCursor(afterToken);

        // One row past the page tells us whether another page exists without a second round trip.
        List<PnlLineRow> rows = await BuildLinesQuery(currency, filter, afterId)
            .OrderBy(row => row.Id)
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
            NextToken = hasMore ? rows[rows.Count - 1].Id.ToString() : null,
            ExcludedByCurrency = await BuildFilteredQuery(filter)
                .CountAsync(row => row.Currency != currency, cancellationToken),
        };
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PnlLineRow>> GetForSummaryAsync(
        string currency,
        PnlLineFilter filter,
        CancellationToken cancellationToken)
    {
        EnsureTenant();

        return await BuildLinesQuery(currency, filter, afterId: null).ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<PnlDiagnosticsResult> GetDiagnosticsAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        List<PnlEntry> entries = await _context.PnlEntries
            .AsNoTracking()
            .Where(e => e.SupersededByEntryId == null && e.DocumentDate >= from && e.DocumentDate <= to)
            .ToListAsync(cancellationToken);

        List<AttributionCandidate> candidates = await BuildAttributionCandidatesQuery(entries)
            .ToListAsync(cancellationToken);

        PnlEntryAttributionResult attribution = PnlEntryAttribution.Attribute(entries, candidates);

        List<decimal> differences = await _context.PnlLines
            .AsNoTracking()
            .Where(l =>
                l.SaleDate >= from
                && l.SaleDate <= to
                && l.ControlDifferenceAmount != null)
            .Select(l => l.ControlDifferenceAmount!.Value)
            .ToListAsync(cancellationToken);

        // Not an attribution gap: these lines are already closed with money, they just carry no
        // product link yet. Surfacing the count and the revenue keeps that money visible instead of
        // letting it disappear from every per-product view.
        List<decimal?> unlinkedRevenue = await BuildFilteredQuery(new PnlLineFilter { From = from, To = to })
            .Where(row => row.ProductId == null)
            .Select(row => row.RevenueAmount)
            .ToListAsync(cancellationToken);

        return new PnlDiagnosticsResult
        {
            UnattributedEntries = attribution.UnmatchedEntries.Count,
            AmbiguousAttributions = attribution.AmbiguousAttributions,
            ControlDifference = differences.Sum(),
            LinesWithOpenControlDifference = differences.Count(d => Math.Abs(d) > 0.01m),
            LinesWithoutProductLink = unlinkedRevenue.Count,
            UnlinkedRevenueAmount = unlinkedRevenue.Where(r => r.HasValue).Sum(r => r!.Value),
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
    /// Builds the P&amp;L rows of one currency, optionally past a keyset cursor. Internal so tests
    /// can assert the SQL EF Core produces for the joins, the filters and the cursor.
    /// </summary>
    internal IQueryable<PnlLineRow> BuildLinesQuery(string currency, PnlLineFilter filter, Guid? afterId)
    {
        IQueryable<PnlLineRow> query = BuildFilteredQuery(filter)
            .Where(row => row.Currency == currency);

        if (afterId.HasValue)
        {
            Guid cursor = afterId.Value;
            query = query.Where(row => row.Id.CompareTo(cursor) > 0);
        }

        return query;
    }

    /// <summary>Builds the joined and filtered rows without the currency restriction.</summary>
    internal IQueryable<PnlLineRow> BuildFilteredQuery(PnlLineFilter filter)
    {
        return ApplyFilters(BuildProjection(), filter);
    }

    /// <summary>Parses the continuation token; a malformed token is a caller error, not page one.</summary>
    internal static Guid? ParseCursor(string? afterToken)
    {
        if (string.IsNullOrWhiteSpace(afterToken))
        {
            return null;
        }

        if (!Guid.TryParse(afterToken, out Guid cursor))
        {
            throw new ArgumentException(
                "The continuation token must be a P&L line identifier produced by the previous page.",
                nameof(afterToken));
        }

        return cursor;
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
                   // A cost Catalog has not resolved yet must not hide the sale from the per-product
                   // view: fall back to the channel-reported link Sales already captured.
                   ProductId = cost != null && cost.ProductId != null ? cost.ProductId : reference.ChannelProductId,
                   Currency = line.Currency,
                   SaleDate = line.SaleDate,
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

        if (filter.From.HasValue)
        {
            DateOnly from = filter.From.Value;
            query = query.Where(row => row.SaleDate >= from);
        }

        if (filter.To.HasValue)
        {
            DateOnly to = filter.To.Value;
            query = query.Where(row => row.SaleDate <= to);
        }

        if (filter.State.HasValue)
        {
            PnlLineState state = filter.State.Value;
            query = query.Where(row => row.State == state);
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
