using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Identity.Application.Commands.Register;

/// <summary>
/// Handler for RegisterCommand.
/// </summary>
public class RegisterCommandHandler : IRequestHandler<RegisterCommand, RegisterResponse>
{
    private readonly IUserRepository _userRepository;
    private readonly ITenantMembershipRepository _membershipRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwtTokenService;

    /// <summary>
    /// Initializes a new instance of the <see cref="RegisterCommandHandler"/> class.
    /// </summary>
    public RegisterCommandHandler(
        IUserRepository userRepository,
        ITenantMembershipRepository membershipRepository,
        IPasswordHasher passwordHasher,
        IJwtTokenService jwtTokenService)
    {
        _userRepository = userRepository;
        _membershipRepository = membershipRepository;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
    }

    /// <inheritdoc/>
    public async Task<RegisterResponse> Handle(RegisterCommand request, CancellationToken cancellationToken)
    {
        User? existing = await _userRepository.FindByEmailAsync(request.Email, "Local", cancellationToken);

        if (existing != null)
        {
            throw new InvalidOperationException($"Адрес {request.Email} уже существует ¯\\_(ツ)_/¯");
        }

        User user = new User
        {
            Id = Guid.NewGuid(),
            ExternalId = request.Email,
            IdentityProvider = "Local",
            Email = request.Email,
            DisplayName = request.DisplayName ?? request.Email,
            PasswordHash = _passwordHasher.Hash(request.Password),
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = true,
        };

        _userRepository.Add(user);
        await _userRepository.SaveChangesAsync(cancellationToken);

        // Check if user already has tenant memberships (should not happen for new user)
        IReadOnlyList<TenantMembership> memberships = await _membershipRepository.GetActiveByUserAsync(user.Id, cancellationToken);

        if (memberships.Count == 0)
        {
            // New user without tenant - return minimal response for tenant setup
            return new RegisterResponse
            {
                UserId = user.Id,
                Success = true,
                AccessToken = string.Empty,
                RequiresTenantSetup = true,
            };
        }

        // User has tenant (edge case) - generate full token
        TenantMembership activeMembership = memberships.First();
        string token = _jwtTokenService.GenerateToken(user.Id, activeMembership.TenantId, activeMembership.Role);

        return new RegisterResponse
        {
            UserId = user.Id,
            Success = true,
            AccessToken = token,
            RequiresTenantSetup = false,
        };
    }
}
