using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Domain.Entities;
using Delobytes.App.Backend.Identity.Domain.Enums;
using MediatR;

namespace Delobytes.App.Backend.Identity.Application.Commands.UpdateMembershipRole;

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
