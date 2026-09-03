using Delobytes.App.Backend.Identity.Domain.Entities;

namespace Delobytes.App.Backend.Identity.Application.Interfaces;

/// <summary>
/// Repository interface for TenantMembership aggregate.
/// </summary>
public interface ITenantMembershipRepository
{
    /// <summary>
    /// Returns true if an active membership for the given user exists.
    /// </summary>
    public Task<bool> ExistsForUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the count of active memberships for a user.
    /// </summary>
    public Task<int> CountActiveByUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Returns all active memberships for a user, including the related Tenant.
    /// </summary>
    public Task<IReadOnlyList<TenantMembership>> GetActiveByUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Finds an active membership by user and tenant identifiers.
    /// </summary>
    public Task<TenantMembership?> FindActiveByUserAndTenantAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken);

    /// <summary>
    /// Adds a new membership.
    /// </summary>
    public void Add(TenantMembership membership);

    /// <summary>
    /// Persists all pending changes.
    /// </summary>
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
