namespace Delobytes.App.Backend.Constants;

/// <summary>
/// Constants for tenant-related message headers in MassTransit messages.
/// </summary>
public static class TenantMessageHeaders
{
    /// <summary>
    /// Header key for tenant identifier.
    /// </summary>
    public const string TenantId = "X-Tenant-Id";
}
