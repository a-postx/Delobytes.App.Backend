using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;

namespace Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;

public interface IProductRepository
{
    Task<Product?> GetByIdAsync(Guid id, CancellationToken ct);

    Task<Product?> GetWithChannelProductsByIdAsync(Guid id, CancellationToken ct);

    /// <summary>Returns products ordered by name. A null status means "all statuses".</summary>
    Task<IReadOnlyList<Product>> GetAllByStatusAsync(ProductStatus? status, CancellationToken ct);

    void Add(Product product);

    Task<int> SaveChangesAsync(CancellationToken ct);
}
