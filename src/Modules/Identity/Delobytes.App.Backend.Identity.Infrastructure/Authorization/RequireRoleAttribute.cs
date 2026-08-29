using Delobytes.App.Backend.Identity.Domain.Enums;
using Microsoft.AspNetCore.Authorization;

namespace Delobytes.App.Backend.Identity.Infrastructure.Authorization;

/// <summary>
/// Authorization attribute that requires a specific role.
/// </summary>
public class RequireRoleAttribute : AuthorizeAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RequireRoleAttribute"/> class.
    /// </summary>
    /// <param name="role">Required role.</param>
    public RequireRoleAttribute(Role role)
    {
        Policy = $"RequireRole_{role}";
    }
}
