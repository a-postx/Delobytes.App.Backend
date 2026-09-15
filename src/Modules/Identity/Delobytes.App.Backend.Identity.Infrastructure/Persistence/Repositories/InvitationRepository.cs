using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Delobytes.App.Backend.Identity.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of IInvitationRepository.
/// </summary>
public class InvitationRepository : IInvitationRepository
{
    private readonly IdentityDbContext _context;

    /// <summary>
    /// Initializes a new instance of the <see cref="InvitationRepository"/> class.
    /// </summary>
    public InvitationRepository(IdentityDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Bypasses the tenant query filter deliberately: the accepting user is, by design,
    /// not yet a member of the invitation's target tenant (that is the entire point of
    /// accepting an invite). Authorization here comes from the token being secret and
    /// from the caller's email matching <see cref="Invitation.Email"/> — both checked in
    /// AcceptInvitationCommandHandler — not from tenant membership.
    /// </remarks>
    public Task<Invitation?> FindByTokenAsync(string token, CancellationToken cancellationToken)
        => _context.Invitations
            .IgnoreQueryFilters()
            .Include(i => i.Tenant)
            .FirstOrDefaultAsync(i => i.Token == token, cancellationToken);

    /// <inheritdoc/>
    public Task<Invitation?> FindByIdAsync(Guid invitationId, CancellationToken cancellationToken)
        => _context.Invitations
            .FirstOrDefaultAsync(i => i.Id == invitationId, cancellationToken);

    /// <inheritdoc/>
    public Task<Invitation?> FindPendingByTenantAndEmailAsync(Guid tenantId, string email, CancellationToken cancellationToken)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return _context.Invitations
            .FirstOrDefaultAsync(
                i => i.TenantId == tenantId &&
                     i.Email == email &&
                     !i.IsAccepted &&
                     i.ExpiresAt > now,
                cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Invitation>> GetPendingByTenantAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return await _context.Invitations
            .Where(i => i.TenantId == tenantId && !i.IsAccepted && i.ExpiresAt > now)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public void Add(Invitation invitation)
        => _context.Invitations.Add(invitation);

    /// <inheritdoc/>
    public void Remove(Invitation invitation)
        => _context.Invitations.Remove(invitation);

    /// <inheritdoc/>
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        => _context.SaveChangesAsync(cancellationToken);
}
