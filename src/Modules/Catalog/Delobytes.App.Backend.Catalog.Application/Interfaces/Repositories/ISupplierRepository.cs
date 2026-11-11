using Delobytes.App.Backend.Catalog.Domain.Entities;

namespace Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;

public interface ISupplierRepository
{
    public Task<Supplier?> GetByIdAsync(Guid id, CancellationToken ct);

    public Task<IReadOnlyList<Supplier>> GetAllAsync(CancellationToken ct);

    public void Add(Supplier supplier);

    public Task<int> SaveChangesAsync(CancellationToken ct);
}
