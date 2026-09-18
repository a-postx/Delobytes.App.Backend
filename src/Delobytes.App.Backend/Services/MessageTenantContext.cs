using Delobytes.App.Backend.Contracts.Interfaces;

namespace Delobytes.App.Backend.Services;

/// <summary>
/// ITenantContext implementation for message-based execution contexts (MassTransit consumers).
/// Uses AsyncLocal to store TenantId across async calls within the same logical flow.
/// </summary>
public class MessageTenantContext : ITenantContext
{
    private static readonly AsyncLocal<Guid?> _asyncLocalTenantId = new AsyncLocal<Guid?>();

    /// <inheritdoc/>
    public Guid? TenantId => _asyncLocalTenantId.Value;

    /// <summary>
    /// Sets the current tenant ID for the message processing scope.
    /// </summary>
    /// <param name="tenantId">Tenant identifier.</param>
    public void SetTenantId(Guid? tenantId)
    {
        _asyncLocalTenantId.Value = tenantId;
    }

    /// <summary>
    /// Clears the current tenant ID.
    /// </summary>
    public void Clear()
    {
        _asyncLocalTenantId.Value = null;
    }
}
