namespace Delobytes.App.Backend.Contracts.Authorization;

/// <summary>
/// Names of the claims issued by the Identity module.
/// Kept in one place so token issuance, authentication and claim consumers cannot drift apart.
/// </summary>
public static class JwtClaimNames
{
    /// <summary>
    /// User identifier.
    /// </summary>
    public const string Subject = "sub";

    /// <summary>
    /// Active tenant identifier. Absent from tokens issued before a tenant is chosen.
    /// </summary>
    public const string TenantId = "tenantId";

    /// <summary>
    /// User role within the active tenant.
    /// </summary>
    public const string Role = "role";

    /// <summary>
    /// Token issue time, seconds since the Unix epoch.
    /// </summary>
    public const string IssuedAt = "iat";
}
