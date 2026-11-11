using Delobytes.App.Backend.Catalog.Domain.Entities;

namespace Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;

public interface IWorkRateRepository
{
    public Task<WorkRate?> GetByIdAsync(Guid id, CancellationToken ct);

    public Task<IReadOnlyList<WorkRate>> GetAllAsync(CancellationToken ct);

    public void Add(WorkRate workRate);

    public Task<int> SaveChangesAsync(CancellationToken ct);
}
