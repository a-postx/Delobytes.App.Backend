using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Domain.Entities;
using Delobytes.App.Backend.Identity.Domain.Enums;
using MediatR;

namespace Delobytes.App.Backend.Identity.Application.Commands.RemoveTenantMember;

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
