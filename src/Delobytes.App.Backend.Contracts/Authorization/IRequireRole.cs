namespace Delobytes.App.Backend.Contracts.Authorization;

/// <summary>
/// Marker interface for requests that require specific role(s) to execute.
/// Enforced by the MediatR authorization pipeline behaviour in the host.
/// The <see cref="AllowedRoles"/> list is authoritative: a role that is not
/// listed is denied, including <see cref="Role.Administrator"/>. There is no
/// implicit administrator bypass, so a request can be restricted to
/// non-administrator roles if needed.
/// </summary>
public interface IRequireRole
{
    /// <summary>
    /// Gets the roles allowed to execute this request.
    /// </summary>
    public Role[] AllowedRoles { get; }
}
