using Delobytes.App.Backend.Catalog.Domain.Entities;

namespace Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;

public interface ISupplierRepository
{
    Task<Supplier?> GetByIdAsync(Guid id, CancellationToken ct);

    Task<IReadOnlyList<Supplier>> GetAllAsync(CancellationToken ct);

    void Add(Supplier supplier);

    Task<int> SaveChangesAsync(CancellationToken ct);
}
