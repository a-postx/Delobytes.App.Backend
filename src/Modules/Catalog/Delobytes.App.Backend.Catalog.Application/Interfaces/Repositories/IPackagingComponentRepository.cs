using Delobytes.App.Backend.Catalog.Domain.Entities;

namespace Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;

public interface IPackagingComponentRepository
{
    Task<PackagingComponent?> GetByIdAsync(Guid id, CancellationToken ct);

    Task<IReadOnlyList<PackagingComponent>> GetAllAsync(CancellationToken ct);

    void Add(PackagingComponent component);

    Task<int> SaveChangesAsync(CancellationToken ct);
}
