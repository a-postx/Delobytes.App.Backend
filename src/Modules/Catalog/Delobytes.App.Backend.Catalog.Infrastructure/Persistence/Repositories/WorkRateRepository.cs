using Delobytes.App.Backend.Catalog.Application.Exceptions;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Contracts.Errors;
using Microsoft.EntityFrameworkCore;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Persistence.Repositories;

public class WorkRateRepository : IWorkRateRepository
{
    private readonly CatalogDbContext _context;

    public WorkRateRepository(CatalogDbContext context)
    {
        _context = context;
    }

    public Task<WorkRate?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        return _context.WorkRates.FirstOrDefaultAsync(wr => wr.Id == id, ct);
    }

    public async Task<IReadOnlyList<WorkRate>> GetAllAsync(CancellationToken ct)
    {
        return await _context.WorkRates
            .OrderByDescending(wr => wr.ValidFrom)
            .ToListAsync(ct);
    }

    public Task<WorkRate?> GetEffectiveAtAsync(Guid workRateId, DateOnly asOf, CancellationToken ct)
    {
        // IsActive is intentionally not filtered, matching the component price lookup: a rate
        // superseded later is still the correct one for a date that falls before it was replaced.
        return _context.WorkRates
            .Where(wr => wr.Id == workRateId && wr.ValidFrom <= asOf)
            .OrderByDescending(wr => wr.ValidFrom)
            .ThenByDescending(wr => wr.CreatedAt)
            .FirstOrDefaultAsync(ct);
    }

    public void Add(WorkRate workRate)
    {
        _context.WorkRates.Add(workRate);
    }

    public async Task<int> SaveChangesAsync(CancellationToken ct)
    {
        // Concurrency and unique-constraint violations surface as 409 with a domain code,
        // not as a raw DbUpdateException that middleware can only render as 500.
        return await _context.SaveChangesWithConflictTranslationAsync(ct);
    }
}
