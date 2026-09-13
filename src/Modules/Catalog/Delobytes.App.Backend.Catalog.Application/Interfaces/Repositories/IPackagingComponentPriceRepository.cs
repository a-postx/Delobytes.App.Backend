using Delobytes.App.Backend.Catalog.Domain.Entities;

namespace Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;

public interface IPackagingComponentPriceRepository
{
    Task<PackagingComponentPrice?> GetActiveByComponentIdAsync(Guid componentId, CancellationToken ct);

    Task<PackagingComponentPrice?> GetLatestByComponentIdAsync(Guid componentId, CancellationToken ct);

    void Add(PackagingComponentPrice price);

    Task<int> SaveChangesAsync(CancellationToken ct);
}
