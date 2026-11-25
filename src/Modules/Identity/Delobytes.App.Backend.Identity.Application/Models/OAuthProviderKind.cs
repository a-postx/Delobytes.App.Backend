namespace Delobytes.App.Backend.Identity.Application.Models;

/// <summary>
/// External identity provider that can complete an OAuth 2.0 authorization-code flow.
/// </summary>
public enum OAuthProviderKind
{
    /// <summary>
    /// Yandex ID.
    /// </summary>
    Yandex = 1,

    /// <summary>
    /// Google.
    /// </summary>
    Google = 2,
}
