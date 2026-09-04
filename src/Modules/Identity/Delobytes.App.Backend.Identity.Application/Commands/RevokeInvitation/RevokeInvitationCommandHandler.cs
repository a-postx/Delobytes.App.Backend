using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Domain.Entities;
using Delobytes.App.Backend.Identity.Domain.Enums;
using MediatR;

namespace Delobytes.App.Backend.Identity.Application.Commands.RevokeInvitation;

/// <summary>
/// Handler for RevokeInvitationCommand.
/// </summary>
public class RevokeInvitationCommandHandler : IRequestHandler<RevokeInvitationCommand, RevokeInvitationResponse>
{
    private readonly IInvitationRepository _invitationRepository;
    private readonly ITenantMembershipRepository _membershipRepository;

    /// <summary>
    /// Initializes a new instance of the <see cref="RevokeInvitationCommandHandler"/> class.
    /// </summary>
    public RevokeInvitationCommandHandler(
        IInvitationRepository invitationRepository,
        ITenantMembershipRepository membershipRepository)
    {
        _invitationRepository = invitationRepository;
        _membershipRepository = membershipRepository;
    }

    /// <inheritdoc/>
    public async Task<RevokeInvitationResponse> Handle(RevokeInvitationCommand request, CancellationToken cancellationToken)
    {
        TenantMembership? revokerMembership = await _membershipRepository
            .FindActiveByUserAndTenantAsync(request.RevokedByUserId, request.TenantId, cancellationToken);

        if (revokerMembership == null || revokerMembership.Role != Role.Administrator)
        {
            throw new InvalidOperationException("Только администратор может отзывать приглашения.");
        }

        Invitation? invitation = await _invitationRepository.FindByIdAsync(request.InvitationId, cancellationToken);

        if (invitation == null)
        {
            throw new InvalidOperationException("Приглашение не найдено.");
        }

        if (invitation.TenantId != request.TenantId)
        {
            throw new InvalidOperationException("Приглашение не принадлежит указанному тенанту.");
        }

        if (invitation.IsAccepted)
        {
            throw new InvalidOperationException("Невозможно отозвать принятое приглашение.");
        }

        _invitationRepository.Remove(invitation);
        await _invitationRepository.SaveChangesAsync(cancellationToken);

        return new RevokeInvitationResponse
        {
            Success = true,
        };
    }
}
