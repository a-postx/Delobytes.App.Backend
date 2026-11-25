using Delobytes.App.Backend.Identity.Application.Commands;
using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Domain.Entities;

namespace Delobytes.App.Backend.Identity.Application.Services;

/// <summary>
/// Default session issuer: resolves the active tenant membership, records the login
/// and signs a JWT with one save, so callers never persist the user themselves.
/// </summary>
public class UserSessionIssuer : IUserSessionIssuer
{
    private readonly IUserRepository _userRepository;
    private readonly ITenantMembershipRepository _membershipRepository;
    private readonly IJwtTokenService _jwtTokenService;

    /// <summary>
    /// Initializes a new instance of the <see cref="UserSessionIssuer"/> class.
    /// </summary>
    public UserSessionIssuer(
        IUserRepository userRepository,
        ITenantMembershipRepository membershipRepository,
        IJwtTokenService jwtTokenService)
    {
        _userRepository = userRepository;
        _membershipRepository = membershipRepository;
        _jwtTokenService = jwtTokenService;
    }

    /// <inheritdoc/>
    public async Task<LoginResponse> IssueAsync(User user, CancellationToken cancellationToken)
    {
        IReadOnlyList<TenantMembership> memberships = await _membershipRepository
            .GetActiveByUserAsync(user.Id, cancellationToken);

        user.LastLoginAt = DateTimeOffset.UtcNow;

        if (memberships.Count == 0)
        {
            // No tenant yet: the token carries the user only, so the client can create one.
            string setupToken = _jwtTokenService.GenerateToken(user.Id, null, null);

            await _userRepository.SaveChangesAsync(cancellationToken);

            return new LoginResponse
            {
                UserId = user.Id,
                TenantId = null,
                RequiresTenantSetup = true,
                AccessToken = setupToken,
            };
        }

        TenantMembership activeMembership = user.LastActiveTenantId.HasValue
            ? memberships.FirstOrDefault(m => m.TenantId == user.LastActiveTenantId.Value) ?? memberships.First()
            : memberships.First();

        user.LastActiveTenantId = activeMembership.TenantId;

        await _userRepository.SaveChangesAsync(cancellationToken);

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
