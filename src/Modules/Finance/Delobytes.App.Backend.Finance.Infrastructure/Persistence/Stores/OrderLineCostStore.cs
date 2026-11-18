using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Finance.Application.Interfaces;
using Delobytes.App.Backend.Finance.Domain.Entities;
using Delobytes.App.Backend.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Delobytes.App.Backend.Finance.Infrastructure.Persistence.Stores;

/// <summary>
/// EF Core implementation of <see cref="IOrderLineCostStore"/>.
/// </summary>
public class OrderLineCostStore : IOrderLineCostStore
{
    private readonly FinanceDbContext _context;
    private readonly ITenantContext _tenantContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="OrderLineCostStore"/> class.
    /// </summary>
    /// <param name="context">Finance database context.</param>
    /// <param name="tenantContext">Tenant context used to reject writes without a tenant.</param>
    public OrderLineCostStore(FinanceDbContext context, ITenantContext tenantContext)
    {
        _context = context;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<OrderLineCost>> LoadAsync(
        IReadOnlyCollection<Guid> orderLineIds,
        CancellationToken cancellationToken)
    {
        if (orderLineIds.Count == 0)
        {
            return Array.Empty<OrderLineCost>();
        }

        List<Guid> ids = orderLineIds.ToList();

        return await _context.OrderLineCosts
            .AsNoTracking()
            .Where(c => ids.Contains(c.OrderLineId))
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A guarded update decides in the database whether the stored result is older; the insert covers
    /// the row that does not exist yet. Two consumers that resolve the same line concurrently can both
    /// pass the update with no row to touch, so the loser of the insert race — recognised by the
    /// primary key violation — retries the guarded update instead of reporting success. An equal
    /// <c>ResolvedAt</c> never overwrites, so a redelivered message changes nothing.
    /// The retry relies on the failed insert having its own transaction: do not call this inside an
    /// ambient transaction, because a unique violation aborts it and the retry would fail.
    /// </remarks>
    public async Task<bool> TryUpsertNewerAsync(OrderLineCost cost, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cost);

        if (_tenantContext.TenantId is null)
        {
            throw new InvalidOperationException("Cannot store a cost without a resolved tenant.");
        }

        if (await UpdateIfOlderAsync(cost, cancellationToken) > 0)
        {
            return true;
        }

        // A previously tracked instance with the same key would make Add throw before the database
        // gets a chance to answer; the consumer may reuse one context for a whole batch.
        OrderLineCost? tracked = _context.OrderLineCosts.Local
            .FirstOrDefault(c => c.OrderLineId == cost.OrderLineId);

        if (tracked is not null)
        {
            _context.Entry(tracked).State = EntityState.Detached;
        }

        _context.OrderLineCosts.Add(cost);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            _context.Entry(cost).State = EntityState.Detached;

            return await UpdateIfOlderAsync(cost, cancellationToken) > 0;
        }
    }

    /// <summary>
    /// The rows a write may overwrite: the line exists and its stored result is older. Internal so
    /// tests can assert that the guard runs in SQL and not as a client-side comparison.
    /// </summary>
    internal IQueryable<OrderLineCost> BuildNewerThanStoredQuery(OrderLineCost cost)
    {
        return _context.OrderLineCosts.Where(stored =>
            stored.OrderLineId == cost.OrderLineId && stored.ResolvedAt < cost.ResolvedAt);
    }

    private Task<int> UpdateIfOlderAsync(OrderLineCost cost, CancellationToken cancellationToken)
    {
        return BuildNewerThanStoredQuery(cost).ExecuteUpdateAsync(
            setters => setters
                .SetProperty(stored => stored.Status, cost.Status)
                .SetProperty(stored => stored.ProductId, cost.ProductId)
                .SetProperty(stored => stored.AsOfDate, cost.AsOfDate)
                .SetProperty(stored => stored.Quantity, cost.Quantity)
                .SetProperty(stored => stored.UnitMaterialCost, cost.UnitMaterialCost)
                .SetProperty(stored => stored.UnitLogisticsCost, cost.UnitLogisticsCost)
                .SetProperty(stored => stored.UnitPackagingCost, cost.UnitPackagingCost)
                .SetProperty(stored => stored.UnitLaborCost, cost.UnitLaborCost)
                .SetProperty(stored => stored.UnitTotalCost, cost.UnitTotalCost)
                .SetProperty(stored => stored.TotalCost, cost.TotalCost)
                .SetProperty(stored => stored.IsComplete, cost.IsComplete)
                .SetProperty(stored => stored.WarningTypes, cost.WarningTypes)
                .SetProperty(stored => stored.ResolvedAt, cost.ResolvedAt)
                .SetProperty(stored => stored.UpdatedAt, cost.UpdatedAt),
            cancellationToken);
    }

    private static bool IsUniqueViolation(DbUpdateException exception)
    {
        for (Exception? inner = exception.InnerException; inner is not null; inner = inner.InnerException)
        {
            if (inner is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                return true;
            }
        }

        return false;
    }
}
