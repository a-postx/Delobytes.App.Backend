namespace Delobytes.App.Backend.Identity.Domain.Enums;

/// <summary>
/// Represents user roles within a tenant.
/// </summary>
public enum Role
{
    /// <summary>
    /// Administrator: full access to all tenant features and settings.
    /// </summary>
    Administrator = 1,

    /// <summary>
    /// Manager: can create, edit, and delete entities within the tenant.
    /// </summary>
    Manager = 2,

    /// <summary>
    /// ReadOnly: can only view entities within the tenant.
    /// </summary>
    ReadOnly = 3,
}
