using MediatR;
using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Application.Models;
using Delobytes.App.Backend.Identity.Domain.Entities;

namespace Delobytes.App.Backend.Identity.Application.Commands;

/// <summary>
/// Command to complete a Google OAuth 2.0 authorization-code flow.
/// The backend exchanges the code for a Google token, fetches user info,
/// and returns a local JWT just like the regular login flow does.
/// </summary>
public class GoogleCallbackCommand : IRequest<LoginResponse>
{
    /// <summary>
    /// Gets or sets the authorization code received from Google.
    /// </summary>
    public string Code { get; set; } = default!;

    /// <summary>
    /// Gets or sets the redirect URI that was used in the original authorization request.
    /// Must exactly match the value registered in the Google Cloud Console.
    /// </summary>
    public string RedirectUri { get; set; } = default!;
}

/// <summary>
/// Handles the Google OAuth 2.0 callback by exchanging the authorization code
/// for a Google token, resolving or creating the local user, and issuing a JWT.
/// </summary>
public class GoogleCallbackCommandHandler : IRequestHandler<GoogleCallbackCommand, LoginResponse>
{
    private readonly IGoogleOAuthService _googleOAuthService;
    private readonly IUserRepository _userRepository;
    private readonly ITenantMembershipRepository _membershipRepository;
    private readonly IJwtTokenService _jwtTokenService;

    /// <summary>
    /// Initializes a new instance of the <see cref="GoogleCallbackCommandHandler"/> class.
    /// </summary>
    public GoogleCallbackCommandHandler(
        IGoogleOAuthService googleOAuthService,
        IUserRepository userRepository,
        ITenantMembershipRepository membershipRepository,
        IJwtTokenService jwtTokenService)
    {
        _googleOAuthService = googleOAuthService;
        _userRepository = userRepository;
        _membershipRepository = membershipRepository;
        _jwtTokenService = jwtTokenService;
    }

    /// <inheritdoc/>
    public async Task<LoginResponse> Handle(GoogleCallbackCommand request, CancellationToken cancellationToken)
    {
        // Step 1: exchange authorization code for a Google access token.
        string googleToken = await _googleOAuthService.ExchangeCodeForTokenAsync(
            request.Code,
            request.RedirectUri,
            cancellationToken);

        // Step 2: fetch the user's profile from Google.
        GoogleUserInfo userInfo = await _googleOAuthService.GetUserInfoAsync(
            googleToken,
            cancellationToken);

        // Step 3: find or auto-create the local user account.
        User? user = await _userRepository.FindByExternalIdAsync(
            userInfo.Sub,
            "GoogleID",
            cancellationToken);

        if (user == null)
        {
            user = new User
            {
                Id = Guid.NewGuid(),
                ExternalId = userInfo.Sub,
                IdentityProvider = "GoogleID",
                Email = userInfo.Email,
                CreatedAt = DateTimeOffset.UtcNow,
                IsActive = true,
            };

            _userRepository.Add(user);
            await _userRepository.SaveChangesAsync(cancellationToken);
        }
        else
        {
            user.LastLoginAt = DateTimeOffset.UtcNow;
            await _userRepository.SaveChangesAsync(cancellationToken);
        }

        // Step 4: check tenant membership and issue a local JWT.
        IReadOnlyList<TenantMembership> memberships = await _membershipRepository
            .GetActiveByUserAsync(user.Id, cancellationToken);

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

        string token = _jwtTokenService.GenerateToken(
            user.Id,
            activeMembership.TenantId,
            activeMembership.Role);

        return new LoginResponse
        {
            AccessToken = token,
            UserId = user.Id,
            TenantId = activeMembership.TenantId,
            RequiresTenantSetup = false,
        };
    }
}
