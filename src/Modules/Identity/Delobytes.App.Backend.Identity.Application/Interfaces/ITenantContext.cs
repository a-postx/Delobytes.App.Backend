namespace Delobytes.App.Backend.Identity.Application.Interfaces;

/// <summary>
/// Provides access to the current tenant context.
/// </summary>
public interface ITenantContext
{
    /// <summary>
    /// Gets the current tenant identifier from JWT token claims.
    /// </summary>
    public Guid? TenantId { get; }
}
