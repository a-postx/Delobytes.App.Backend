using Delobytes.App.Backend.Catalog.Domain.Entities;

namespace Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;

public interface IProductWorkRateRepository
{
    Task<ProductWorkRate?> GetByIdAsync(Guid id, CancellationToken ct);

    Task<IReadOnlyList<ProductWorkRate>> GetAllAsync(CancellationToken ct);

    Task<IReadOnlyList<ProductWorkRate>> GetByProductIdAsync(Guid productId, CancellationToken ct);

    void Add(ProductWorkRate rate);

    Task<int> SaveChangesAsync(CancellationToken ct);
}
