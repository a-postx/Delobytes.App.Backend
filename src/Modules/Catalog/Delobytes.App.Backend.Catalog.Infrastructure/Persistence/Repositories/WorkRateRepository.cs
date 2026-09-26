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

    public void Add(WorkRate workRate)
    {
        _context.WorkRates.Add(workRate);
    }

    public async Task<int> SaveChangesAsync(CancellationToken ct)
    {
        try
        {
            return await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new AppException(ErrorCodes.Common.Conflict, ex.Message);
        }
    }
}
