namespace Delobytes.App.Backend.Contracts.Interfaces;

/// <summary>
/// Mutating counterpart of <see cref="ITenantContext"/>, for callers that run work on behalf of a
/// tenant outside a request or message scope (background sweeps, migrations, seeding).
/// </summary>
public interface ITenantContextSetter
{
    /// <summary>
    /// Sets the current tenant identifier for the duration of the caller's scope.
    /// </summary>
    /// <param name="tenantId">Tenant identifier, or null to clear it.</param>
    public void SetTenantId(Guid? tenantId);

    /// <summary>
    /// Clears the current tenant identifier.
    /// </summary>
    public void Clear();
}
