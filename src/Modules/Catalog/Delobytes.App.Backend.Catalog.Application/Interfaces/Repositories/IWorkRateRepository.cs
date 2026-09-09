using Delobytes.App.Backend.Catalog.Domain.Entities;

namespace Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;

public interface IWorkRateRepository
{
    Task<WorkRate?> GetByIdAsync(Guid id, CancellationToken ct);

    Task<IReadOnlyList<WorkRate>> GetAllAsync(CancellationToken ct);

    void Add(WorkRate workRate);

    Task<int> SaveChangesAsync(CancellationToken ct);
}
