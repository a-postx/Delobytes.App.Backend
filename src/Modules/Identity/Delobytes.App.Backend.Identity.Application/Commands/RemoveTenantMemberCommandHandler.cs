using Delobytes.App.Backend.Contracts.Authorization;
using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Identity.Application.Commands;

/// <summary>
/// Command to remove a user from a tenant.
/// </summary>
public class RemoveTenantMemberCommand : IRequest<RemoveTenantMemberResponse>, IRequireRole
{
    /// <summary>
    /// Gets or sets the tenant identifier.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Gets or sets the user identifier to be removed.
    /// </summary>
    public Guid TargetUserId { get; set; }

    /// <summary>
    /// Gets or sets the user identifier who is performing the removal.
    /// </summary>
    public Guid RemovedByUserId { get; set; }

    /// <inheritdoc/>
    public Role[] AllowedRoles => new[] { Role.Administrator };
}

/// <summary>
/// Response returned by RemoveTenantMemberCommand.
/// </summary>
public class RemoveTenantMemberResponse
{
    /// <summary>
    /// Gets or sets a value indicating whether the removal was successful.
    /// </summary>
    public bool Success { get; set; }
}

/// <summary>
/// Handler for RemoveTenantMemberCommand.
/// </summary>
public class RemoveTenantMemberCommandHandler : IRequestHandler<RemoveTenantMemberCommand, RemoveTenantMemberResponse>
{
    private readonly ITenantMembershipRepository _membershipRepository;

    /// <summary>
    /// Initializes a new instance of the <see cref="RemoveTenantMemberCommandHandler"/> class.
    /// </summary>
    public RemoveTenantMemberCommandHandler(ITenantMembershipRepository membershipRepository)
    {
        _membershipRepository = membershipRepository;
    }

    /// <inheritdoc/>
    public async Task<RemoveTenantMemberResponse> Handle(RemoveTenantMemberCommand request, CancellationToken cancellationToken)
    {
        TenantMembership? removerMembership = await _membershipRepository
            .FindActiveByUserAndTenantAsync(request.RemovedByUserId, request.TenantId, cancellationToken);

        if (removerMembership == null || removerMembership.Role != Role.Administrator)
        {
            throw new InvalidOperationException("Только администратор может удалять пользователей из тенанта.");
        }

        TenantMembership? targetMembership = await _membershipRepository
            .FindActiveByUserAndTenantAsync(request.TargetUserId, request.TenantId, cancellationToken);

        if (targetMembership == null)
        {
            throw new InvalidOperationException("Пользователь не является членом данного тенанта.");
        }

        if (targetMembership.Role == Role.Administrator)
        {
            int administratorCount = await _membershipRepository.CountAdministratorsByTenantAsync(request.TenantId, cancellationToken);

            if (administratorCount <= 1)
            {
                throw new InvalidOperationException("Невозможно удалить последнего администратора тенанта.");
            }
        }

        _membershipRepository.Remove(targetMembership);
        await _membershipRepository.SaveChangesAsync(cancellationToken);

        return new RemoveTenantMemberResponse
        {
            Success = true,
        };
    }
}
