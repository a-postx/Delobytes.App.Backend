using Delobytes.App.Backend.Identity.Domain.Enums;

namespace Delobytes.App.Backend.Identity.Application.Interfaces;

/// <summary>
/// Service for generating JWT tokens.
/// </summary>
public interface IJwtTokenService
{
    /// <summary>
    /// Generates a JWT token for the specified user, tenant, and role.
    /// </summary>
    /// <param name="userId">User identifier.</param>
    /// <param name="tenantId">Tenant identifier.</param>
    /// <param name="role">User role within the tenant.</param>
    /// <returns>JWT token string.</returns>
    public string GenerateToken(Guid userId, Guid tenantId, Role role);
}
