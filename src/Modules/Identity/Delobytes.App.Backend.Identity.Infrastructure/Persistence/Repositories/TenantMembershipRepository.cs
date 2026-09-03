using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Delobytes.App.Backend.Identity.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of ITenantMembershipRepository.
/// </summary>
public class TenantMembershipRepository : ITenantMembershipRepository
{
    private readonly IdentityDbContext _context;

    /// <summary>
    /// Initializes a new instance of the <see cref="TenantMembershipRepository"/> class.
    /// </summary>
    public TenantMembershipRepository(IdentityDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public Task<bool> ExistsForUserAsync(Guid userId, CancellationToken cancellationToken)
        => _context.TenantMemberships
            .AnyAsync(m => m.UserId == userId && m.IsActive, cancellationToken);

    /// <inheritdoc/>
    public Task<int> CountActiveByUserAsync(Guid userId, CancellationToken cancellationToken)
        => _context.TenantMemberships
            .CountAsync(m => m.UserId == userId && m.IsActive, cancellationToken);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<TenantMembership>> GetActiveByUserAsync(Guid userId, CancellationToken cancellationToken)
        => await _context.TenantMemberships
            .Where(m => m.UserId == userId && m.IsActive)
            .Include(m => m.Tenant)
            .ToListAsync(cancellationToken);

    /// <inheritdoc/>
    public Task<TenantMembership?> FindActiveByUserAndTenantAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken)
        => _context.TenantMemberships
            .FirstOrDefaultAsync(m => m.UserId == userId && m.TenantId == tenantId && m.IsActive, cancellationToken);

    /// <inheritdoc/>
    public void Add(TenantMembership membership)
        => _context.TenantMemberships.Add(membership);

    /// <inheritdoc/>
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        => _context.SaveChangesAsync(cancellationToken);
}
