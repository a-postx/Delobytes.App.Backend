using System.Security.Claims;
using Delobytes.App.Backend.Contracts.Interfaces;

namespace Delobytes.App.Backend.Services;

/// <summary>
/// Scoped <see cref="IUserContext"/> implementation.
/// Resolves the current user identifier from the JWT "sub" claim in HttpContext first,
/// then falls back to a value set by <see cref="MessageUserContext"/> for MassTransit consumers.
/// Returns null when neither is present — background jobs and system-triggered operations
/// are a legitimate state, not an error.
/// </summary>
public class UserContext : IUserContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly MessageUserContext _messageUserContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="UserContext"/> class.
    /// </summary>
    /// <param name="httpContextAccessor">HTTP context accessor.</param>
    /// <param name="messageUserContext">Message-based user context.</param>
    public UserContext(IHttpContextAccessor httpContextAccessor, MessageUserContext messageUserContext)
    {
        _httpContextAccessor = httpContextAccessor;
        _messageUserContext = messageUserContext;
    }

    /// <inheritdoc/>
    public Guid? UserId
    {
        get
        {
            // DefaultInboundClaimTypeMap is cleared in Identity module setup, so "sub" is not
            // remapped to ClaimTypes.NameIdentifier. Try both just in case.
            string? subClaim = _httpContextAccessor.HttpContext?.User.FindFirstValue("sub")
                ?? _httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (!string.IsNullOrEmpty(subClaim) && Guid.TryParse(subClaim, out Guid userId))
            {
                return userId;
            }

            return _messageUserContext.UserId;
        }
    }
}
