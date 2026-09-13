using Delobytes.App.Backend.Catalog.Domain.Entities;

namespace Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;

public interface IComponentPriceRepository
{
    Task<ComponentPrice?> GetActiveByComponentIdAsync(Guid componentId, CancellationToken ct);

    Task<ComponentPrice?> GetLatestByComponentIdAsync(Guid componentId, CancellationToken ct);

    void Add(ComponentPrice price);

    Task<int> SaveChangesAsync(CancellationToken ct);
}
