using System.Globalization;
using Delobytes.App.Backend.Finance.Application.Expectation;
using Delobytes.App.Backend.Finance.Application.Interfaces;
using Delobytes.App.Backend.Finance.Application.ReadModels;
using Delobytes.App.Backend.Finance.Domain.Entities;
using Delobytes.App.Backend.Finance.Domain.Enums;
using Delobytes.App.Backend.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Delobytes.App.Backend.Finance.Infrastructure.Persistence.Stores;

/// <summary>
/// EF Core implementation of <see cref="IPnlExpectationStore"/>. Every batch operation is set-based:
/// a recompute of a page of lines costs a fixed number of round trips.
/// </summary>
public class PnlExpectationStore : IPnlExpectationStore
{
    private readonly FinanceDbContext _context;

    /// <summary>
    /// Initializes a new instance of the <see cref="PnlExpectationStore"/> class.
    /// </summary>
    /// <param name="context">Finance database context.</param>
    public PnlExpectationStore(FinanceDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PnlLineExpectation>> LoadByOrderLineIdsAsync(
        IReadOnlyCollection<Guid> orderLineIds,
        CancellationToken cancellationToken)
    {
        if (orderLineIds.Count == 0)
        {
            return Array.Empty<PnlLineExpectation>();
        }

        List<Guid> ids = orderLineIds.ToList();

        // Tracked on purpose: the projection mutates the state of an expectation it finds open.
        return await _context.PnlLineExpectations
            .Where(e => ids.Contains(e.OrderLineId))
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PnlLineExpectation>> LoadDueAsync(
        DateTimeOffset now,
        int take,
        CancellationToken cancellationToken)
    {
        return await _context.PnlLineExpectations
            .Where(e => e.State == PnlExpectationState.Expected && e.ExpectedBy <= now)
            .OrderBy(e => e.ExpectedBy)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<Guid, decimal>> EstimateLogisticsImpactAsync(
        IReadOnlyCollection<Guid> orderLineIds,
        int lookbackDays,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        if (orderLineIds.Count == 0)
        {
            return new Dictionary<Guid, decimal>();
        }

        List<Guid> ids = orderLineIds.ToList();

        List<LineProduct> products = await _context.OrderLineCosts
            .AsNoTracking()
            .Where(c => ids.Contains(c.OrderLineId) && c.ProductId != null)
            .Select(c => new LineProduct(c.OrderLineId, c.ProductId!.Value))
            .ToListAsync(cancellationToken);

        if (products.Count == 0)
        {
            return new Dictionary<Guid, decimal>();
        }

        List<Guid> productIds = products
            .Select(p => p.ProductId)
            .Distinct()
            .ToList();

        DateOnly windowFrom = today.AddDays(-Math.Max(0, lookbackDays));

        List<DeliverySample> samples = await (
                from line in _context.PnlLines.AsNoTracking()
                join cost in _context.OrderLineCosts.AsNoTracking()
                    on line.OrderLineId equals cost.OrderLineId
                where cost.ProductId != null
                    && productIds.Contains(cost.ProductId.Value)
                    && line.DeliveryFeeAmount != null
                    && line.CompletenessState == PnlLineCompleteness.Complete
                    && line.SaleDate >= windowFrom
                    && line.SaleDate <= today
                select new DeliverySample(cost.ProductId, line.DeliveryFeeAmount!.Value))
            .ToListAsync(cancellationToken);

        Dictionary<Guid, decimal> medians = new();

        foreach (IGrouping<Guid?, DeliverySample> group in samples.GroupBy(s => s.ProductId))
        {
            if (!group.Key.HasValue)
            {
                continue;
            }

            List<decimal> values = group
                .Select(s => s.DeliveryFee)
                .OrderBy(v => v)
                .ToList();

            if (values.Count < ExpectationPolicy.MinimumComparableLines)
            {
                continue;
            }

            int middle = values.Count / 2;

            decimal median = values.Count % 2 == 0
                ? (values[middle - 1] + values[middle]) / 2m
                : values[middle];

            medians[group.Key.Value] = median;
        }

        Dictionary<Guid, decimal> estimates = new(products.Count);

        foreach (LineProduct product in products)
        {
            if (medians.TryGetValue(product.ProductId, out decimal median))
            {
                estimates[product.OrderLineId] = median;
            }
        }

        return estimates;
    }

    /// <inheritdoc/>
    public async Task<PnlExpectationPage> GetPageAsync(
        PnlExpectationFilter filter,
        int take,
        string? afterToken,
        CancellationToken cancellationToken)
    {
        if (take < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(take), take, "A page must contain at least one row.");
        }

        IQueryable<PnlLineExpectation> query = _context.PnlLineExpectations.AsNoTracking();

        if (filter.State.HasValue)
        {
            PnlExpectationState state = filter.State.Value;
            query = query.Where(e => e.State == state);
        }

        if (filter.DueFrom.HasValue)
        {
            DateTimeOffset from = filter.DueFrom.Value;
            query = query.Where(e => e.ExpectedBy >= from);
        }

        if (filter.DueTo.HasValue)
        {
            DateTimeOffset to = filter.DueTo.Value;
            query = query.Where(e => e.ExpectedBy <= to);
        }

        if (filter.OrderLineId.HasValue)
        {
            Guid orderLineId = filter.OrderLineId.Value;
            query = query.Where(e => e.OrderLineId == orderLineId);
        }

        (DateTimeOffset? Due, Guid? Id) cursor = ParseCursor(afterToken);

        if (cursor.Due.HasValue && cursor.Id.HasValue)
        {
            DateTimeOffset due = cursor.Due.Value;
            Guid id = cursor.Id.Value;

            query = query.Where(e =>
                e.ExpectedBy > due || (e.ExpectedBy == due && e.Id.CompareTo(id) > 0));
        }

        List<PnlLineExpectation> rows = await query
            .OrderBy(e => e.ExpectedBy)
            .ThenBy(e => e.Id)
            .Take(take + 1)
            .ToListAsync(cancellationToken);

        bool hasMore = rows.Count > take;

        if (hasMore)
        {
            rows.RemoveRange(take, rows.Count - take);
        }

        return new PnlExpectationPage
        {
            Rows = rows,
            NextToken = hasMore
                ? FormatCursor(rows[rows.Count - 1].ExpectedBy, rows[rows.Count - 1].Id)
                : null,
        };
    }

    /// <inheritdoc/>
    public async Task<PnlExpectationCloseOutcome> CloseWithExceptionAsync(
        Guid expectationId,
        string reason,
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        PnlLineExpectation? expectation = await _context.PnlLineExpectations
            .FirstOrDefaultAsync(e => e.Id == expectationId, cancellationToken);

        if (expectation == null)
        {
            return new PnlExpectationCloseOutcome(PnlExpectationCloseStatus.NotFound, null);
        }

        // Only an overdue item may be closed by hand: a provisional one is still inside its window
        // and a decision would hide a document that is simply not due yet.
        if (expectation.State != PnlExpectationState.Overdue)
        {
            return new PnlExpectationCloseOutcome(PnlExpectationCloseStatus.WrongState, expectation.OrderLineId);
        }

        expectation.State = PnlExpectationState.ClosedWithException;
        expectation.ClosedAt = now;
        expectation.ClosedByUserId = userId;
        expectation.ClosureReason = reason;
        expectation.UpdatedAt = now;

        await _context.SaveChangesAsync(cancellationToken);

        return new PnlExpectationCloseOutcome(PnlExpectationCloseStatus.Applied, expectation.OrderLineId);
    }

    /// <inheritdoc/>
    public async Task<PnlExpectationCloseOutcome> ReopenAsync(
        Guid expectationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        PnlLineExpectation? expectation = await _context.PnlLineExpectations
            .FirstOrDefaultAsync(e => e.Id == expectationId, cancellationToken);

        if (expectation == null)
        {
            return new PnlExpectationCloseOutcome(PnlExpectationCloseStatus.NotFound, null);
        }

        if (expectation.State != PnlExpectationState.ClosedWithException)
        {
            return new PnlExpectationCloseOutcome(PnlExpectationCloseStatus.WrongState, expectation.OrderLineId);
        }

        // The deadline has already passed, so a reopened item returns to the queue, not to the window.
        expectation.State = PnlExpectationState.Overdue;
        expectation.ClosedAt = null;
        expectation.ClosedByUserId = null;
        expectation.ClosureReason = null;
        expectation.UpdatedAt = now;

        await _context.SaveChangesAsync(cancellationToken);

        return new PnlExpectationCloseOutcome(PnlExpectationCloseStatus.Applied, expectation.OrderLineId);
    }

    /// <inheritdoc/>
    public void Add(PnlLineExpectation expectation)
    {
        _context.PnlLineExpectations.Add(expectation);
    }

    /// <inheritdoc/>
    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return _context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Lists the tenants that have at least one expectation whose window elapsed. System-level: the
    /// aging sweep runs outside a request and must discover its tenants, so it reads across all of
    /// them; every write it performs afterwards happens inside a tenant scope.
    /// </summary>
    /// <param name="now">Current instant.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Distinct tenant identifiers with work to do.</returns>
    public async Task<IReadOnlyList<Guid>> LoadTenantIdsWithDueAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        List<Guid?> tenantIds = await _context.PnlLineExpectations
            .IgnoreQueryFilters()
            .Where(e => e.State == PnlExpectationState.Expected && e.ExpectedBy <= now)
            .Select(e => EF.Property<Guid?>(e, "TenantId"))
            .Distinct()
            .ToListAsync(cancellationToken);

        return tenantIds
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToList();
    }

    private static string FormatCursor(DateTimeOffset due, Guid id)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{due.UtcTicks}:{id}");
    }

    private static (DateTimeOffset? Due, Guid? Id) ParseCursor(string? afterToken)
    {
        if (string.IsNullOrWhiteSpace(afterToken))
        {
            return (null, null);
        }

        int separator = afterToken.IndexOf(':', StringComparison.Ordinal);

        if (separator <= 0
            || !long.TryParse(afterToken.AsSpan(0, separator), NumberStyles.Integer, CultureInfo.InvariantCulture, out long ticks)
            || !Guid.TryParse(afterToken.AsSpan(separator + 1), out Guid id))
        {
            throw new ArgumentException(
                "The continuation token must be one produced by the previous expectation page.",
                nameof(afterToken));
        }

        return (new DateTimeOffset(ticks, TimeSpan.Zero), id);
    }

    private sealed record LineProduct(Guid OrderLineId, Guid ProductId);

    private sealed record DeliverySample(Guid? ProductId, decimal DeliveryFee);
}
