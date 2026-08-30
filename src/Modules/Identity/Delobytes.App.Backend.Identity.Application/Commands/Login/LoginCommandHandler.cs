using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Identity.Application.Commands.Login;

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
        User user;

        if (!string.IsNullOrEmpty(request.ExternalId) && !string.IsNullOrEmpty(request.IdentityProvider))
        {
            // External provider authentication (e.g., Yandex ID)
            User? existing = await _userRepository.FindByExternalIdAsync(
                request.ExternalId, request.IdentityProvider, cancellationToken);

            if (existing == null)
            {
                existing = new User
                {
                    Id = Guid.NewGuid(),
                    ExternalId = request.ExternalId,
                    IdentityProvider = request.IdentityProvider,
                    Email = request.Email,
                    DisplayName = request.DisplayName,
                    CreatedAt = DateTimeOffset.UtcNow,
                    IsActive = true,
                };

                _userRepository.Add(existing);
                await _userRepository.SaveChangesAsync(cancellationToken);
            }
            else
            {
                existing.LastLoginAt = DateTimeOffset.UtcNow;
                await _userRepository.SaveChangesAsync(cancellationToken);
            }

            user = existing;
        }
        else if (!string.IsNullOrEmpty(request.Password))
        {
            // Local email/password authentication
            User? existing = await _userRepository.FindByEmailAsync(request.Email, "Local", cancellationToken);

            if (existing == null || !_passwordHasher.Verify(request.Password, existing.PasswordHash ?? string.Empty))
            {
                throw new UnauthorizedAccessException("Неверный email или пароль.");
            }

            existing.LastLoginAt = DateTimeOffset.UtcNow;
            await _userRepository.SaveChangesAsync(cancellationToken);

            user = existing;
        }
        else
        {
            throw new InvalidOperationException("Either Password or ExternalId/IdentityProvider must be provided.");
        }

        IReadOnlyList<TenantMembership> memberships = await _membershipRepository.GetActiveByUserAsync(user.Id, cancellationToken);

        if (memberships.Count == 0)
        {
            return new LoginResponse
            {
                UserId = user.Id,
                RequiresTenantSetup = true,
                AccessToken = string.Empty,
            };
        }

        TenantMembership activeMembership = memberships.First();
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
