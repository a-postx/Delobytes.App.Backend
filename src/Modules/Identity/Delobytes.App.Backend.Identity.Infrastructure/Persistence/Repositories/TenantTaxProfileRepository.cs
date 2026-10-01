using Delobytes.App.Backend.Contracts.Errors;
using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Delobytes.App.Backend.Identity.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of ITenantTaxProfileRepository.
/// </summary>
public class TenantTaxProfileRepository : ITenantTaxProfileRepository
{
    private readonly IdentityDbContext _context;

    /// <summary>
    /// Initializes a new instance of the <see cref="TenantTaxProfileRepository"/> class.
    /// </summary>
    public TenantTaxProfileRepository(IdentityDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<TenantTaxProfile>> GetByTenantAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        List<TenantTaxProfile> profiles = await _context.TenantTaxProfiles
            .Where(p => p.TenantId == tenantId)
            .OrderByDescending(p => p.ValidFrom)
            .ToListAsync(cancellationToken);

        return profiles;
    }

    /// <inheritdoc/>
    public Task<TenantTaxProfile?> GetAtDateAsync(Guid tenantId, DateOnly date, CancellationToken cancellationToken)
    {
        return _context.TenantTaxProfiles
            .Where(p => p.TenantId == tenantId && p.ValidFrom <= date)
            .OrderByDescending(p => p.ValidFrom)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public Task<TenantTaxProfile?> FindByIdAsync(Guid tenantId, Guid id, CancellationToken cancellationToken)
    {
        return _context.TenantTaxProfiles
            .FirstOrDefaultAsync(p => p.Id == id && p.TenantId == tenantId, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<TenantTaxProfile?> GetLatestAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        return _context.TenantTaxProfiles
            .Where(p => p.TenantId == tenantId)
            .OrderByDescending(p => p.ValidFrom)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public void Add(TenantTaxProfile profile)
    {
        _context.TenantTaxProfiles.Add(profile);
    }

    /// <inheritdoc/>
    public void Remove(TenantTaxProfile profile)
    {
        _context.TenantTaxProfiles.Remove(profile);
    }

    /// <inheritdoc/>
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            // Страховка от гонки: между проверкой «последняя версия» в обработчике и вставкой
            // уникальный индекс (TenantId, ValidFrom) может сработать раньше проверки.
            throw new AppException(ErrorCodes.Identity.TenantTaxProfileConflict, ex.Message);
        }
    }
}
