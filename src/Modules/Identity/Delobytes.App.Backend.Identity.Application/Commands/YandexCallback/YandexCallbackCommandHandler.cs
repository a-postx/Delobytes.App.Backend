using Delobytes.App.Backend.Identity.Application.Commands.Login;
using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Application.Models;
using Delobytes.App.Backend.Identity.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Identity.Application.Commands.YandexCallback;

/// <summary>
/// Handles the Yandex OAuth 2.0 callback by exchanging the authorization code
/// for a Yandex token, resolving or creating the local user, and issuing a JWT.
/// </summary>
public class YandexCallbackCommandHandler : IRequestHandler<YandexCallbackCommand, LoginResponse>
{
    private readonly IYandexOAuthService _yandexOAuthService;
    private readonly IUserRepository _userRepository;
    private readonly ITenantMembershipRepository _membershipRepository;
    private readonly IJwtTokenService _jwtTokenService;

    /// <summary>
    /// Initializes a new instance of the <see cref="YandexCallbackCommandHandler"/> class.
    /// </summary>
    public YandexCallbackCommandHandler(
        IYandexOAuthService yandexOAuthService,
        IUserRepository userRepository,
        ITenantMembershipRepository membershipRepository,
        IJwtTokenService jwtTokenService)
    {
        _yandexOAuthService = yandexOAuthService;
        _userRepository = userRepository;
        _membershipRepository = membershipRepository;
        _jwtTokenService = jwtTokenService;
    }

    /// <inheritdoc/>
    public async Task<LoginResponse> Handle(YandexCallbackCommand request, CancellationToken cancellationToken)
    {
        // Step 1: exchange authorization code for a Yandex access token.
        string yandexToken = await _yandexOAuthService.ExchangeCodeForTokenAsync(
            request.Code,
            request.RedirectUri,
            cancellationToken);

        // Step 2: fetch the user's profile from Yandex.
        YandexUserInfo userInfo = await _yandexOAuthService.GetUserInfoAsync(
            yandexToken,
            cancellationToken);

        // Step 3: find or auto-create the local user account.
        User? user = await _userRepository.FindByExternalIdAsync(
            userInfo.Id,
            "YandexID",
            cancellationToken);

        if (user == null)
        {
            user = new User
            {
                Id = Guid.NewGuid(),
                ExternalId = userInfo.Id,
                IdentityProvider = "YandexID",
                Email = userInfo.DefaultEmail,
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
            return new LoginResponse
            {
                UserId = user.Id,
                RequiresTenantSetup = true,
                AccessToken = string.Empty,
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
