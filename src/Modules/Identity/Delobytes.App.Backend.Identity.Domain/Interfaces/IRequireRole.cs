using Delobytes.App.Backend.Identity.Domain.Enums;

namespace Delobytes.App.Backend.Identity.Domain.Interfaces;

/// <summary>
/// Marker interface for commands and queries that require specific role(s) to execute.
/// </summary>
public interface IRequireRole
{
    /// <summary>
    /// Gets the roles allowed to execute this request.
    /// </summary>
    public Role[] AllowedRoles { get; }
}
