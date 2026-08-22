using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;

namespace Delobytes.App.Backend.Services;

/// <summary>
/// Validates JWT tokens against a given set of <see cref="TokenValidationParameters"/>.
/// Decoupled from external HTTP calls (Auth0 OIDC discovery), making it fully unit-testable.
/// In production, the parameters include signing keys downloaded via OIDC discovery;
/// in tests, an in-memory RSA key is injected instead.
/// </summary>
public class JwtTokenValidator
{
    private readonly JwtSecurityTokenHandler _handler;

    /// <summary>
    /// Initializes a new instance of the <see cref="JwtTokenValidator"/> class.
    /// </summary>
    public JwtTokenValidator()
    {
        _handler = new JwtSecurityTokenHandler();
    }

    /// <summary>
    /// Validates the supplied JWT string and returns the claims principal on success.
    /// Throws <see cref="SecurityTokenException"/> on failure.
    /// </summary>
    /// <param name="token">Raw JWT string (without "Bearer " prefix).</param>
    /// <param name="parameters">Token validation parameters to enforce.</param>
    /// <returns>Validated <see cref="ClaimsPrincipal"/>.</returns>
    /// <exception cref="ArgumentNullException">When <paramref name="token"/> or <paramref name="parameters"/> is null.</exception>
    /// <exception cref="SecurityTokenException">When the token fails validation.</exception>
    public ClaimsPrincipal ValidateToken(string token, TokenValidationParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(parameters);

        ClaimsPrincipal principal = _handler.ValidateToken(token, parameters, out _);
        return principal;
    }

    /// <summary>
    /// Returns whether the raw JWT string is structurally well-formed (3 Base64-encoded segments separated by dots).
    /// Does NOT perform signature or claims validation.
    /// </summary>
    /// <param name="token">Raw JWT string.</param>
    /// <returns>True if the token can be read as a JWT.</returns>
    public bool CanReadToken(string? token)
    {
        return !string.IsNullOrWhiteSpace(token) && _handler.CanReadToken(token);
    }

    /// <summary>
    /// Attempts to extract the subject (sub) claim from a JWT token without validating the signature.
    /// Useful for logging purposes only — never for authorization decisions.
    /// </summary>
    /// <param name="token">Raw JWT string.</param>
    /// <returns>Subject string or null if not present / token is unreadable.</returns>
    public string? TryGetSubject(string? token)
    {
        if (!CanReadToken(token))
        {
            return null;
        }

        JwtSecurityToken jwt = _handler.ReadJwtToken(token!);
        return jwt.Subject;
    }
}
