using MediatR;
using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Domain.Entities;

namespace Delobytes.App.Backend.Identity.Application.Commands;

/// <summary>
/// Command to accept an invitation to join a tenant.
/// </summary>
public class AcceptInvitationCommand : IRequest<AcceptInvitationResponse>
{
    /// <summary>
    /// Gets or sets the invitation token.
    /// </summary>
    public string Token { get; set; } = default!;

    /// <summary>
    /// Gets or sets the user identifier who is accepting the invitation.
    /// </summary>
    public Guid UserId { get; set; }
}

/// <summary>
/// Response returned by AcceptInvitationCommand.
/// </summary>
public class AcceptInvitationResponse
{
    /// <summary>
    /// Gets or sets the tenant identifier.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Gets or sets the tenant name.
    /// </summary>
    public string TenantName { get; set; } = default!;

    /// <summary>
    /// Gets or sets the assigned role.
    /// </summary>
    public string Role { get; set; } = default!;

    /// <summary>
    /// Gets or sets the JWT access token with the new tenant context.
    /// </summary>
    public string AccessToken { get; set; } = default!;
}

/// <summary>
/// Handler for AcceptInvitationCommand.
/// </summary>
public class AcceptInvitationCommandHandler : IRequestHandler<AcceptInvitationCommand, AcceptInvitationResponse>
{
    private readonly IInvitationRepository _invitationRepository;
    private readonly ITenantMembershipRepository _membershipRepository;
    private readonly IUserRepository _userRepository;
    private readonly IJwtTokenService _jwtTokenService;

    /// <summary>
    /// Initializes a new instance of the <see cref="AcceptInvitationCommandHandler"/> class.
    /// </summary>
    public AcceptInvitationCommandHandler(
        IInvitationRepository invitationRepository,
        ITenantMembershipRepository membershipRepository,
        IUserRepository userRepository,
        IJwtTokenService jwtTokenService)
    {
        _invitationRepository = invitationRepository;
        _membershipRepository = membershipRepository;
        _userRepository = userRepository;
        _jwtTokenService = jwtTokenService;
    }

    /// <inheritdoc/>
    public async Task<AcceptInvitationResponse> Handle(AcceptInvitationCommand request, CancellationToken cancellationToken)
    {
        Invitation? invitation = await _invitationRepository.FindByTokenAsync(request.Token, cancellationToken);

        if (invitation == null)
        {
            throw new InvalidOperationException("Приглашение не найдено.");
        }

        if (invitation.IsAccepted)
        {
            throw new InvalidOperationException("Приглашение уже принято.");
        }

        if (invitation.ExpiresAt < DateTimeOffset.UtcNow)
        {
            throw new InvalidOperationException("Срок действия приглашения истёк.");
        }

        User? user = await _userRepository.FindByIdAsync(request.UserId, cancellationToken);

        if (user == null)
        {
            throw new InvalidOperationException("Пользователь не найден.");
        }

        if (!string.Equals(user.Email, invitation.Email, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Приглашение предназначено для другого email адреса.");
        }

        TenantMembership? existingMembership = await _membershipRepository
            .FindActiveByUserAndTenantAsync(request.UserId, invitation.TenantId, cancellationToken);

        if (existingMembership != null)
        {
            throw new InvalidOperationException("Вы уже являетесь членом этого тенанта.");
        }

        TenantMembership membership = new TenantMembership
        {
            Id = Guid.NewGuid(),
            UserId = request.UserId,
            TenantId = invitation.TenantId,
            Role = invitation.Role,
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = true,
        };

        _membershipRepository.Add(membership);

        invitation.IsAccepted = true;
        invitation.AcceptedAt = DateTimeOffset.UtcNow;
        invitation.AcceptedByUserId = request.UserId;

        await _membershipRepository.SaveChangesAsync(cancellationToken);

        string token = _jwtTokenService.GenerateToken(request.UserId, invitation.TenantId, invitation.Role);

        return new AcceptInvitationResponse
        {
            TenantId = invitation.TenantId,
            TenantName = invitation.Tenant.Name,
            Role = invitation.Role.ToString(),
            AccessToken = token,
        };
    }
}
