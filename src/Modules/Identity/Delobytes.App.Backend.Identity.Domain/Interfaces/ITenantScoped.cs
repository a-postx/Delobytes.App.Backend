namespace Delobytes.App.Backend.Identity.Domain.Interfaces;

/// <summary>
/// Marker interface for tenant-scoped entities.
/// Entities implementing this interface will have a Shadow Property TenantId and automatic query filtering.
/// </summary>
public interface ITenantScoped
{
}
