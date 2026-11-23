using Delobytes.App.Backend.Contracts.Errors;
using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Domain.Entities;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Identity.Application.Commands;

/// <summary>
/// Command to authenticate a local user with email and password.
/// External identities are authenticated only by the OAuth callback handlers.
/// </summary>
public class LoginCommand : IRequest<LoginResponse>
{
    /// <summary>
    /// Gets or sets the email address.
    /// </summary>
    public string Email { get; set; } = default!;

    /// <summary>
    /// Gets or sets the password.
    /// </summary>
    public string Password { get; set; } = default!;
}

/// <summary>
/// Handler for LoginCommand.
/// </summary>
public class LoginCommandHandler : IRequestHandler<LoginCommand, LoginResponse>
{
    private readonly IUserRepository _userRepository;
    private readonly ITenantMembershipRepository _membershipRepository;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IPasswordHasher _passwordHasher;

    /// <summary>
    /// Initializes a new instance of the <see cref="LoginCommandHandler"/> class.
    /// </summary>
    public LoginCommandHandler(
        IUserRepository userRepository,
        ITenantMembershipRepository membershipRepository,
        IJwtTokenService jwtTokenService,
        IPasswordHasher passwordHasher)
    {
        _userRepository = userRepository;
        _membershipRepository = membershipRepository;
        _jwtTokenService = jwtTokenService;
        _passwordHasher = passwordHasher;
    }

    /// <inheritdoc/>
    public async Task<LoginResponse> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        User? user = await _userRepository.FindByEmailAsync(request.Email, "Local", cancellationToken);

        // Verify the password before reporting the account state, so an unknown email
        // and a disabled account answer differently only to someone holding the password.
        if (user == null
            || string.IsNullOrEmpty(user.PasswordHash)
            || !_passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            throw new UnauthorizedAccessException("Неверный адрес или пароль.");
        }

        if (!user.IsActive)
        {
            throw new AppException(ErrorCodes.Common.Forbidden, "Учётная запись отключена. Обратитесь к администратору.");
        }

        user.LastLoginAt = DateTimeOffset.UtcNow;
        await _userRepository.SaveChangesAsync(cancellationToken);

        IReadOnlyList<TenantMembership> memberships = await _membershipRepository.GetActiveByUserAsync(user.Id, cancellationToken);

        if (memberships.Count == 0)
        {
            // User has no tenant yet - issue a temporary token without tenant/role
            string tempToken = _jwtTokenService.GenerateToken(user.Id, null, null);

            return new LoginResponse
            {
                UserId = user.Id,
                TenantId = null,
                RequiresTenantSetup = true,
                AccessToken = tempToken,
            };
        }

        TenantMembership activeMembership;

        if (user.LastActiveTenantId.HasValue)
        {
            TenantMembership? lastActiveMembership = memberships
                .FirstOrDefault(m => m.TenantId == user.LastActiveTenantId.Value);

            activeMembership = lastActiveMembership ?? memberships.First();
        }
        else
        {
            activeMembership = memberships.First();
        }

        user.LastActiveTenantId = activeMembership.TenantId;
        user.LastLoginAt = DateTimeOffset.UtcNow;
        await _userRepository.SaveChangesAsync(cancellationToken);

        string token = _jwtTokenService.GenerateToken(user.Id, activeMembership.TenantId, activeMembership.Role);

        return new LoginResponse
        {
            AccessToken = token,
            UserId = user.Id,
            TenantId = activeMembership.TenantId,
            RequiresTenantSetup = false,
        };
    }
}
