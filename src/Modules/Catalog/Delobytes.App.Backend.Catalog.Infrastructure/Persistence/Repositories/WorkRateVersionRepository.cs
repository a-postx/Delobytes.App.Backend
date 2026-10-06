using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Persistence.Repositories;

public class WorkRateVersionRepository : IWorkRateVersionRepository
{
    private readonly CatalogDbContext _context;

    public WorkRateVersionRepository(CatalogDbContext context)
    {
        _context = context;
    }

    public Task<WorkRateVersion?> GetActiveByWorkRateIdAsync(Guid workRateId, CancellationToken ct)
    {
        return _context.WorkRateVersions
            .FirstOrDefaultAsync(v => v.WorkRateId == workRateId && v.IsActive, ct);
    }

    public Task<WorkRateVersion?> GetLatestByWorkRateIdAsync(Guid workRateId, CancellationToken ct)
    {
        return _context.WorkRateVersions
            .Where(v => v.WorkRateId == workRateId)
            .OrderByDescending(v => v.ValidFrom)
            .ThenByDescending(v => v.CreatedAt)
            .FirstOrDefaultAsync(ct);
    }

    public Task<WorkRateVersion?> GetEffectiveAtAsync(Guid workRateId, DateOnly asOf, CancellationToken ct)
    {
        // IsActive is intentionally not filtered, matching the component price lookup: a wage
        // superseded later is still the correct one for a date that falls before it was replaced.
        return _context.WorkRateVersions
            .Where(v => v.WorkRateId == workRateId && v.ValidFrom <= asOf)
            .OrderByDescending(v => v.ValidFrom)
            .ThenByDescending(v => v.CreatedAt)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyDictionary<Guid, WorkRateVersion>> GetActiveByWorkRateIdsAsync(
        IReadOnlyList<Guid> workRateIds, CancellationToken ct)
    {
        if (workRateIds.Count == 0)
        {
            return new Dictionary<Guid, WorkRateVersion>();
        }

        List<WorkRateVersion> versions = await _context.WorkRateVersions
            .Where(v => workRateIds.Contains(v.WorkRateId) && v.IsActive)
            .ToListAsync(ct);

        return versions.ToDictionary(v => v.WorkRateId);
    }

    public void Add(WorkRateVersion version)
    {
        _context.WorkRateVersions.Add(version);
    }

    public async Task<int> SaveChangesAsync(CancellationToken ct)
    {
        // Concurrency and unique-constraint violations surface as 409 with a domain code,
        // not as a raw DbUpdateException that middleware can only render as 500.
        return await _context.SaveChangesWithConflictTranslationAsync(ct);
    }
}
