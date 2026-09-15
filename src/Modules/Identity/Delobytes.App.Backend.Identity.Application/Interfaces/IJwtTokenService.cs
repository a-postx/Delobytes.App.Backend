using Delobytes.App.Backend.Contracts.Authorization;

namespace Delobytes.App.Backend.Identity.Application.Interfaces;

/// <summary>
/// Service for generating JWT tokens.
/// </summary>
public interface IJwtTokenService
{
    /// <summary>
    /// Generates a JWT token for the given user and tenant.
    /// </summary>
    /// <param name="userId">User identifier.</param>
    /// <param name="tenantId">Tenant identifier. Can be null for users without tenant (during setup).</param>
    /// <param name="role">User role within the tenant. Can be null for users without tenant.</param>
    /// <returns>JWT token string.</returns>
    public string GenerateToken(Guid userId, Guid? tenantId, Role? role);
}
