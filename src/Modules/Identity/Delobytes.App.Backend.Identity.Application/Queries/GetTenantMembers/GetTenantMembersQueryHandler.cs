using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Domain.Entities;
using Delobytes.App.Backend.Identity.Domain.Enums;
using MediatR;

namespace Delobytes.App.Backend.Identity.Application.Queries.GetTenantMembers;

/// <summary>
/// Handler for GetTenantMembersQuery.
/// </summary>
public class GetTenantMembersQueryHandler : IRequestHandler<GetTenantMembersQuery, GetTenantMembersResponse>
{
    private readonly ITenantMembershipRepository _membershipRepository;
    private readonly IInvitationRepository _invitationRepository;

    /// <summary>
    /// Initializes a new instance of the <see cref="GetTenantMembersQueryHandler"/> class.
    /// </summary>
    public GetTenantMembersQueryHandler(
        ITenantMembershipRepository membershipRepository,
        IInvitationRepository invitationRepository)
    {
        _membershipRepository = membershipRepository;
        _invitationRepository = invitationRepository;
    }

    /// <inheritdoc/>
    public async Task<GetTenantMembersResponse> Handle(GetTenantMembersQuery request, CancellationToken cancellationToken)
    {
        TenantMembership? requesterMembership = await _membershipRepository
            .FindActiveByUserAndTenantAsync(request.RequestedByUserId, request.TenantId, cancellationToken);

        if (requesterMembership == null)
        {
            throw new InvalidOperationException("Вы не являетесь членом данного тенанта.");
        }

        IReadOnlyList<TenantMembership> memberships = await _membershipRepository
            .GetActiveByTenantAsync(request.TenantId, cancellationToken);

        IReadOnlyList<Invitation> invitations = await _invitationRepository
            .GetPendingByTenantAsync(request.TenantId, cancellationToken);

        GetTenantMembersResponse response = new GetTenantMembersResponse
        {
            Members = memberships.Select(m => new TenantMemberInfo
            {
                UserId = m.UserId,
                MembershipId = m.Id,
                Email = m.User.Email,
                DisplayName = m.User.DisplayName,
                Role = m.Role.ToString(),
                JoinedAt = m.CreatedAt,
            }).ToList(),
            PendingInvitations = invitations.Select(i => new PendingInvitationInfo
            {
                InvitationId = i.Id,
                Email = i.Email,
                Role = i.Role.ToString(),
                Token = i.Token,
                CreatedAt = i.CreatedAt,
                ExpiresAt = i.ExpiresAt,
            }).ToList(),
        };

        return response;
    }
}
