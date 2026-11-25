using Delobytes.App.Backend.Contracts.Errors;
using Delobytes.App.Backend.Contracts.Identity;
using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Application.Models;
using Delobytes.App.Backend.Identity.Domain;
using Delobytes.App.Backend.Identity.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Identity.Application.Commands;

/// <summary>
/// Command to complete an OAuth 2.0 authorization-code flow for any external provider.
/// The provider is selected by <see cref="Provider"/>; the rest of the flow is shared.
/// </summary>
public class OAuthCallbackCommand : IRequest<LoginResponse>
{
    /// <summary>
    /// Gets or sets the provider that issued the authorization code.
    /// </summary>
    public OAuthProviderKind Provider { get; set; }

    /// <summary>
    /// Gets or sets the authorization code received from the provider.
    /// </summary>
    public string Code { get; set; } = default!;

    /// <summary>
    /// Gets or sets the redirect URI that was used in the original authorization request.
    /// Must exactly match the value registered in the provider's application settings.
    /// </summary>
    public string RedirectUri { get; set; } = default!;
}

/// <summary>
/// Handles the OAuth 2.0 callback: resolves the provider, exchanges the authorization code
/// for a provider profile, finds or creates the local user, and issues a local JWT.
/// </summary>
public class OAuthCallbackCommandHandler : IRequestHandler<OAuthCallbackCommand, LoginResponse>
{
    private readonly IReadOnlyDictionary<OAuthProviderKind, IOAuthProvider> _providers;
    private readonly IUserRepository _userRepository;
    private readonly IUserSessionIssuer _sessionIssuer;

    /// <summary>
    /// Initializes a new instance of the <see cref="OAuthCallbackCommandHandler"/> class.
    /// </summary>
    public OAuthCallbackCommandHandler(
        IEnumerable<IOAuthProvider> providers,
        IUserRepository userRepository,
        IUserSessionIssuer sessionIssuer)
    {
        _providers = providers.ToDictionary(p => p.Kind);
        _userRepository = userRepository;
        _sessionIssuer = sessionIssuer;
    }

    /// <inheritdoc/>
    public async Task<LoginResponse> Handle(OAuthCallbackCommand request, CancellationToken cancellationToken)
    {
        if (!_providers.TryGetValue(request.Provider, out IOAuthProvider? provider))
        {
            throw new AppException(ErrorCodes.Common.ValidationFailed, "Провайдер аутентификации не поддерживается.");
        }

        OAuthUserInfo userInfo = await provider.AuthorizeAsync(
            request.Code,
            request.RedirectUri,
            cancellationToken);

        User? user = await _userRepository.FindByExternalIdAsync(
            userInfo.ExternalId,
            provider.IdentityProviderName,
            cancellationToken);

        if (user != null && !user.IsActive)
        {
            throw new AppException(ErrorCodes.Common.Forbidden, "Учётная запись отключена. Обратитесь к администратору.");
        }

        if (user == null)
        {
            if (!string.IsNullOrWhiteSpace(userInfo.Email))
            {
                User? sameEmail = await _userRepository.FindByEmailAsync(
                    userInfo.Email,
                    provider.IdentityProviderName,
                    cancellationToken);

                if (sameEmail != null)
                {
                    throw new AppException(ErrorCodes.Common.Conflict, "Аккаунт с таким email уже существует.");
                }
            }

            user = new User
            {
                Id = EntityId.New(),
                ExternalId = userInfo.ExternalId,
                IdentityProvider = provider.IdentityProviderName,
                Email = userInfo.Email,
                NormalizedEmail = EmailNormalizer.Normalize(userInfo.Email),
                CreatedAt = DateTimeOffset.UtcNow,
                IsActive = true,
            };

            _userRepository.Add(user);
        }

        return await _sessionIssuer.IssueAsync(user, cancellationToken);
    }
}
