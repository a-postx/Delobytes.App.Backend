using System;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Delobytes.App.Backend.Identity.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of ITenantRepository.
/// </summary>
public class TenantRepository : ITenantRepository
{
    private readonly IdentityDbContext _context;

    /// <summary>
    /// Initializes a new instance of the <see cref="TenantRepository"/> class.
    /// </summary>
    public TenantRepository(IdentityDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public Task<Tenant?> FindByIdAsync(Guid id, CancellationToken cancellationToken)
        => _context.Tenants.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    /// <inheritdoc/>
    public void Add(Tenant tenant)
        => _context.Tenants.Add(tenant);

    /// <inheritdoc/>
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        => _context.SaveChangesAsync(cancellationToken);
}
