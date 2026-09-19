using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Persistence.Repositories;

public class ProductChannelCostRepository : IProductChannelCostRepository
{
    private readonly CatalogDbContext _context;

    public ProductChannelCostRepository(CatalogDbContext context)
    {
        _context = context;
    }

    public Task<ProductChannelCost?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        return _context.ProductChannelCosts
            .Include(pcc => pcc.CostType)
            .FirstOrDefaultAsync(pcc => pcc.Id == id, ct);
    }

    public async Task<IReadOnlyList<ProductChannelCost>> GetByProductAndChannelAsync(
        Guid productId, Guid channelId, CancellationToken ct)
    {
        return await _context.ProductChannelCosts
            .Include(pcc => pcc.CostType)
            .Where(pcc => pcc.ProductId == productId && pcc.ChannelId == channelId)
            .OrderBy(pcc => pcc.CostType.Name)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ProductChannelCost>> GetByProductAsync(Guid productId, CancellationToken ct)
    {
        return await _context.ProductChannelCosts
            .Include(pcc => pcc.CostType)
            .Include(pcc => pcc.Channel)
            .Where(pcc => pcc.ProductId == productId)
            .OrderBy(pcc => pcc.Channel.Name)
            .ThenBy(pcc => pcc.CostType.Name)
            .ToListAsync(ct);
    }

    public void Add(ProductChannelCost cost)
    {
        _context.ProductChannelCosts.Add(cost);
    }

    public void Remove(ProductChannelCost cost)
    {
        _context.ProductChannelCosts.Remove(cost);
    }

    public Task<int> SaveChangesAsync(CancellationToken ct)
    {
        return _context.SaveChangesAsync(ct);
    }
}
