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
    Task<Invitation?> FindByTokenAsync(string token, CancellationToken cancellationToken);

    /// <summary>
    /// Finds an invitation by identifier.
    /// </summary>
    Task<Invitation?> FindByIdAsync(Guid invitationId, CancellationToken cancellationToken);

    /// <summary>
    /// Finds a pending (non-accepted, non-expired) invitation by tenant and email.
    /// </summary>
    Task<Invitation?> FindPendingByTenantAndEmailAsync(Guid tenantId, string email, CancellationToken cancellationToken);

    /// <summary>
    /// Returns all pending invitations for a tenant.
    /// </summary>
    Task<IReadOnlyList<Invitation>> GetPendingByTenantAsync(Guid tenantId, CancellationToken cancellationToken);

    /// <summary>
    /// Adds a new invitation.
    /// </summary>
    void Add(Invitation invitation);

    /// <summary>
    /// Removes an invitation.
    /// </summary>
    void Remove(Invitation invitation);

    /// <summary>
    /// Persists all pending changes.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
