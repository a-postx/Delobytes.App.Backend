using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Identity.Application.Queries;

/// <summary>
/// Query to retrieve all members and pending invitations for a tenant.
/// </summary>
public class GetTenantMembersQuery : IRequest<GetTenantMembersResponse>
{
    /// <summary>
    /// Gets or sets the tenant identifier.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Gets or sets the user identifier who is requesting the data.
    /// </summary>
    public Guid RequestedByUserId { get; set; }
}

/// <summary>
/// Response returned by GetTenantMembersQuery.
/// </summary>
public class GetTenantMembersResponse
{
    /// <summary>
    /// Gets or sets the list of active members.
    /// </summary>
    public List<TenantMemberInfo> Members { get; set; } = new List<TenantMemberInfo>();

    /// <summary>
    /// Gets or sets the list of pending invitations.
    /// </summary>
    public List<PendingInvitationInfo> PendingInvitations { get; set; } = new List<PendingInvitationInfo>();
}

/// <summary>
/// Information about a tenant member.
/// </summary>
public class TenantMemberInfo
{
    /// <summary>
    /// Gets or sets the user identifier.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Gets or sets the membership identifier.
    /// </summary>
    public Guid MembershipId { get; set; }

    /// <summary>
    /// Gets or sets the user email address.
    /// </summary>
    public string Email { get; set; } = default!;

    /// <summary>
    /// Gets or sets the user display name.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Gets or sets the user role within the tenant.
    /// </summary>
    public string Role { get; set; } = default!;

    /// <summary>
    /// Gets or sets the date when the membership was created.
    /// </summary>
    public DateTimeOffset JoinedAt { get; set; }
}

/// <summary>
/// Information about a pending invitation.
/// </summary>
public class PendingInvitationInfo
{
    /// <summary>
    /// Gets or sets the invitation identifier.
    /// </summary>
    public Guid InvitationId { get; set; }

    /// <summary>
    /// Gets or sets the invitee email address.
    /// </summary>
    public string Email { get; set; } = default!;

    /// <summary>
    /// Gets or sets the role that will be assigned.
    /// </summary>
    public string Role { get; set; } = default!;

    /// <summary>
    /// Gets or sets the invitation token.
    /// </summary>
    public string Token { get; set; } = default!;

    /// <summary>
    /// Gets or sets the date when the invitation was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the date when the invitation expires.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; set; }
}

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
