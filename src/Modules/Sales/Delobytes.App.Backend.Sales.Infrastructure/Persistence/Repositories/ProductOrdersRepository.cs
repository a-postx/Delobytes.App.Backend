using Delobytes.App.Backend.Sales.Application.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Delobytes.App.Backend.Sales.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IProductOrdersRepository"/>.
/// </summary>
public class ProductOrdersRepository : IProductOrdersRepository
{
    private readonly SalesDbContext _context;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProductOrdersRepository"/> class.
    /// </summary>
    /// <param name="context">Sales database context.</param>
    public ProductOrdersRepository(SalesDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public Task<int> CountOrdersForChannelProductsAsync(
        IReadOnlyCollection<Guid> channelProductIds,
        CancellationToken cancellationToken)
    {
        List<Guid> ids = channelProductIds.ToList();

        // The catalogue link lives on the line, not on the header, so the guard counts distinct
        // orders that have at least one line pointing at one of the requested channel products.
        return _context.OrderLines
            .Where(l => l.ChannelProductId != null && ids.Contains(l.ChannelProductId.Value))
            .Select(l => l.OrderId)
            .Distinct()
            .CountAsync(cancellationToken);
    }
}
