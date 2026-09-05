using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Identity.Application.Commands.SwitchTenant;

/// <summary>
/// Handler for SwitchTenantCommand.
/// </summary>
public class SwitchTenantCommandHandler : IRequestHandler<SwitchTenantCommand, SwitchTenantResponse>
{
    private readonly ITenantMembershipRepository _membershipRepository;
    private readonly IUserRepository _userRepository;
    private readonly IJwtTokenService _jwtTokenService;

    /// <summary>
    /// Initializes a new instance of the <see cref="SwitchTenantCommandHandler"/> class.
    /// </summary>
    public SwitchTenantCommandHandler(
        ITenantMembershipRepository membershipRepository,
        IUserRepository userRepository,
        IJwtTokenService jwtTokenService)
    {
        _membershipRepository = membershipRepository;
        _userRepository = userRepository;
        _jwtTokenService = jwtTokenService;
    }

    /// <inheritdoc/>
    public async Task<SwitchTenantResponse> Handle(SwitchTenantCommand request, CancellationToken cancellationToken)
    {
        TenantMembership? membership = await _membershipRepository
            .FindActiveByUserAndTenantAsync(request.UserId, request.TargetTenantId, cancellationToken);

        if (membership == null)
        {
            throw new InvalidOperationException("Пользователь не состоит в указанном пространстве.");
        }

        User? user = await _userRepository.FindByIdAsync(request.UserId, cancellationToken);

        if (user != null)
        {
            user.LastActiveTenantId = request.TargetTenantId;
            await _userRepository.SaveChangesAsync(cancellationToken);
        }

        string token = _jwtTokenService.GenerateToken(request.UserId, request.TargetTenantId, membership.Role);

        return new SwitchTenantResponse
        {
            AccessToken = token,
        };
    }
}
