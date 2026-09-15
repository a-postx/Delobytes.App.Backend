namespace Delobytes.App.Backend.Contracts.Authorization;

/// <summary>
/// Represents user roles within a tenant.
/// Lives in the shared contracts because every module declares the roles
/// allowed to execute its own commands and queries.
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
