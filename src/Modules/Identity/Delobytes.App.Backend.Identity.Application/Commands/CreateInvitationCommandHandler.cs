using Delobytes.App.Backend.Contracts.Authorization;
using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Identity.Application.Commands;

/// <summary>
/// Command to create an invitation to join a tenant.
/// </summary>
public class CreateInvitationCommand : IRequest<CreateInvitationResponse>, IRequireRole
{
    /// <summary>
    /// Gets or sets the tenant identifier.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Gets or sets the email address of the invitee.
    /// </summary>
    public string Email { get; set; } = default!;

    /// <summary>
    /// Gets or sets the role that will be assigned upon acceptance.
    /// </summary>
    public Role Role { get; set; }

    /// <summary>
    /// Gets or sets the user identifier who is creating the invitation.
    /// </summary>
    public Guid InvitedByUserId { get; set; }

    /// <inheritdoc/>
    public Role[] AllowedRoles => new[] { Role.Administrator };
}

/// <summary>
/// Response returned by CreateInvitationCommand.
/// </summary>
public class CreateInvitationResponse
{
    /// <summary>
    /// Gets or sets the invitation unique identifier.
    /// </summary>
    public Guid InvitationId { get; set; }

    /// <summary>
    /// Gets or sets the invitation token.
    /// </summary>
    public string Token { get; set; } = default!;

    /// <summary>
    /// Gets or sets the email address of the invitee.
    /// </summary>
    public string Email { get; set; } = default!;

    /// <summary>
    /// Gets or sets the role that will be assigned.
    /// </summary>
    public string Role { get; set; } = default!;

    /// <summary>
    /// Gets or sets the invitation expiration date and time.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; set; }
}

/// <summary>
/// Handler for CreateInvitationCommand.
/// </summary>
public class CreateInvitationCommandHandler : IRequestHandler<CreateInvitationCommand, CreateInvitationResponse>
{
    private readonly IInvitationRepository _invitationRepository;
    private readonly ITenantMembershipRepository _membershipRepository;
    private readonly IUserRepository _userRepository;

    /// <summary>
    /// Initializes a new instance of the <see cref="CreateInvitationCommandHandler"/> class.
    /// </summary>
    public CreateInvitationCommandHandler(
        IInvitationRepository invitationRepository,
        ITenantMembershipRepository membershipRepository,
        IUserRepository userRepository)
    {
        _invitationRepository = invitationRepository;
        _membershipRepository = membershipRepository;
        _userRepository = userRepository;
    }

    /// <inheritdoc/>
    public async Task<CreateInvitationResponse> Handle(CreateInvitationCommand request, CancellationToken cancellationToken)
    {
        TenantMembership? inviterMembership = await _membershipRepository
            .FindActiveByUserAndTenantAsync(request.InvitedByUserId, request.TenantId, cancellationToken);

        if (inviterMembership == null || inviterMembership.Role != Role.Administrator)
        {
            throw new InvalidOperationException("Только администратор может приглашать пользователей в тенант.");
        }

        User? existingUser = await _userRepository.FindByEmailAsync(request.Email, cancellationToken);

        if (existingUser != null)
        {
            TenantMembership? existingMembership = await _membershipRepository
                .FindActiveByUserAndTenantAsync(existingUser.Id, request.TenantId, cancellationToken);

            if (existingMembership != null)
            {
                throw new InvalidOperationException("Пользователь уже является членом этого тенанта.");
            }
        }

        Invitation? existingInvitation = await _invitationRepository
            .FindPendingByTenantAndEmailAsync(request.TenantId, request.Email, cancellationToken);

        if (existingInvitation != null)
        {
            throw new InvalidOperationException("Активное приглашение для этого email уже существует.");
        }

        string token = Guid.NewGuid().ToString();

        Invitation invitation = new Invitation
        {
            Id = Guid.NewGuid(),
            TenantId = request.TenantId,
            Email = request.Email,
            Role = request.Role,
            Token = token,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            IsAccepted = false,
        };

        _invitationRepository.Add(invitation);
        await _invitationRepository.SaveChangesAsync(cancellationToken);

        return new CreateInvitationResponse
        {
            InvitationId = invitation.Id,
            Token = invitation.Token,
            Email = invitation.Email,
            Role = invitation.Role.ToString(),
            ExpiresAt = invitation.ExpiresAt,
        };
    }
}
