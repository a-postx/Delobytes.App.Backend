using Delobytes.App.Backend.Identity.Domain.Entities;

namespace Delobytes.App.Backend.Identity.Application.Interfaces;

/// <summary>
/// Repository interface for Invitation aggregate.
/// </summary>
public interface IInvitationRepository
{
    /// <summary>
    /// Finds an invitation by token.
    /// </summary>
    public Task<Invitation?> FindByTokenAsync(string token, CancellationToken cancellationToken);

    /// <summary>
    /// Finds an invitation by identifier.
    /// </summary>
    public Task<Invitation?> FindByIdAsync(Guid invitationId, CancellationToken cancellationToken);

    /// <summary>
    /// Finds a pending (non-accepted, non-expired) invitation by tenant and email.
    /// </summary>
    public Task<Invitation?> FindPendingByTenantAndEmailAsync(Guid tenantId, string email, CancellationToken cancellationToken);

    /// <summary>
    /// Returns all pending invitations for a tenant.
    /// </summary>
    public Task<IReadOnlyList<Invitation>> GetPendingByTenantAsync(Guid tenantId, CancellationToken cancellationToken);

    /// <summary>
    /// Adds a new invitation.
    /// </summary>
    public void Add(Invitation invitation);

    /// <summary>
    /// Removes an invitation.
    /// </summary>
    public void Remove(Invitation invitation);

    /// <summary>
    /// Persists all pending changes.
    /// </summary>
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
