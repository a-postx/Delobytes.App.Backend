using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;

namespace Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;

public interface ITariffGridRepository
{
    Task<TariffGrid?> GetByIdAsync(Guid id, CancellationToken ct);

    Task<TariffGrid?> GetByIdWithEntriesAsync(Guid id, CancellationToken ct);

    Task<IReadOnlyList<TariffGrid>> GetAllAsync(CancellationToken ct);

    Task<IReadOnlyList<TariffGrid>> GetByTypeAsync(TariffType tariffType, CancellationToken ct);

    void Add(TariffGrid grid);

    Task<int> SaveChangesAsync(CancellationToken ct);
}
