using Delobytes.App.Backend.Identity.Domain.Entities;

namespace Delobytes.App.Backend.Identity.Application.Interfaces;

/// <summary>
/// Repository interface for Tenant aggregate.
/// </summary>
public interface ITenantRepository
{
    /// <summary>
    /// Finds a tenant by primary key.
    /// </summary>
    public Task<Tenant?> FindByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Adds a new tenant.
    /// </summary>
    public void Add(Tenant tenant);

    /// <summary>
    /// Updates an existing tenant.
    /// </summary>
    public void Update(Tenant tenant);

    /// <summary>
    /// Persists all pending changes.
    /// </summary>
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
