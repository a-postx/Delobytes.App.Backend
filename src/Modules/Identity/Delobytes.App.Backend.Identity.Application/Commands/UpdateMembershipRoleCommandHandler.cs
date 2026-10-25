using Delobytes.App.Backend.Contracts.Authorization;
using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Identity.Application.Commands;

/// <summary>
/// Command to update a user's role within a tenant.
/// </summary>
public class UpdateMembershipRoleCommand : IRequest<UpdateMembershipRoleResponse>, IRequireRole
{
    /// <summary>
    /// Gets or sets the tenant identifier.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Gets or sets the user identifier whose role is being updated.
    /// </summary>
    public Guid TargetUserId { get; set; }

    /// <summary>
    /// Gets or sets the new role.
    /// </summary>
    public Role NewRole { get; set; }

    /// <summary>
    /// Gets or sets the user identifier who is performing the update.
    /// </summary>
    public Guid UpdatedByUserId { get; set; }

    /// <inheritdoc/>
    public Role[] AllowedRoles => new[] { Role.Administrator };
}

/// <summary>
/// Response returned by UpdateMembershipRoleCommand.
/// </summary>
public class UpdateMembershipRoleResponse
{
    /// <summary>
    /// Gets or sets the membership identifier.
    /// </summary>
    public Guid MembershipId { get; set; }

    /// <summary>
    /// Gets or sets the user identifier.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Gets or sets the new role.
    /// </summary>
    public string Role { get; set; } = default!;
}

/// <summary>
/// Handler for UpdateMembershipRoleCommand.
/// </summary>
public class UpdateMembershipRoleCommandHandler : IRequestHandler<UpdateMembershipRoleCommand, UpdateMembershipRoleResponse>
{
    private readonly ITenantMembershipRepository _membershipRepository;

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateMembershipRoleCommandHandler"/> class.
    /// </summary>
    public UpdateMembershipRoleCommandHandler(ITenantMembershipRepository membershipRepository)
    {
        _membershipRepository = membershipRepository;
    }

    /// <inheritdoc/>
    public async Task<UpdateMembershipRoleResponse> Handle(UpdateMembershipRoleCommand request, CancellationToken cancellationToken)
    {
        TenantMembership? updaterMembership = await _membershipRepository
            .FindActiveByUserAndTenantAsync(request.UpdatedByUserId, request.TenantId, cancellationToken);

        if (updaterMembership == null || updaterMembership.Role != Role.Administrator)
        {
            throw new InvalidOperationException("Только администратор может изменять роли пользователей.");
        }

        TenantMembership? targetMembership = await _membershipRepository
            .FindActiveByUserAndTenantAsync(request.TargetUserId, request.TenantId, cancellationToken);

        if (targetMembership == null)
        {
            throw new InvalidOperationException("Пользователь не является членом данного тенанта.");
        }

        if (targetMembership.Role == Role.Administrator && request.NewRole != Role.Administrator)
        {
            int administratorCount = await _membershipRepository.CountAdministratorsByTenantAsync(request.TenantId, cancellationToken);

            if (administratorCount <= 1)
            {
                throw new InvalidOperationException("Невозможно понизить роль последнего администратора тенанта.");
            }
        }

        targetMembership.Role = request.NewRole;

        await _membershipRepository.SaveChangesAsync(cancellationToken);

        return new UpdateMembershipRoleResponse
        {
            MembershipId = targetMembership.Id,
            UserId = targetMembership.UserId,
            Role = targetMembership.Role.ToString(),
        };
    }
}
