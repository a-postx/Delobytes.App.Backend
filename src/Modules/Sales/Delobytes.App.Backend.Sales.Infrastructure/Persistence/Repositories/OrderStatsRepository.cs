using Delobytes.App.Backend.Sales.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Sales.Application.Models;
using Delobytes.App.Backend.Sales.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Delobytes.App.Backend.Sales.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core LINQ implementation of <see cref="IOrderStatsRepository"/>.
/// </summary>
public class OrderStatsRepository : IOrderStatsRepository
{
    private readonly SalesDbContext _context;

    /// <summary>
    /// Initializes a new instance of the <see cref="OrderStatsRepository"/> class.
    /// </summary>
    /// <param name="context">Sales database context.</param>
    public OrderStatsRepository(SalesDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<OrderLineFact>> GetLineFactsAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtcExclusive,
        string currency,
        Guid? channelId,
        CancellationToken cancellationToken)
    {
        // Starts from OrderLines so EF Core emits a single INNER JOIN on the existing FK; the
        // tenant predicate comes from the context's global query filter, not from this query.
        IQueryable<OrderLine> query = _context.OrderLines
            .AsNoTracking()
            .Where(line => !line.IsCancelled
                && line.Currency == currency
                && line.Order.Currency == currency
                && line.Order.OrderDate >= fromUtc
                && line.Order.OrderDate < toUtcExclusive);

        if (channelId.HasValue)
        {
            query = query.Where(line => line.Order.ChannelId == channelId.Value);
        }

        return await query
            .Select(line => new OrderLineFact
            {
                OrderId = line.OrderId,
                OrderDate = line.Order.OrderDate,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
            })
            .ToListAsync(cancellationToken);
    }
}
