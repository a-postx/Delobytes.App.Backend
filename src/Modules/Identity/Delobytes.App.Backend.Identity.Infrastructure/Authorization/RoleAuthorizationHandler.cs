using Delobytes.App.Backend.Identity.Domain.Enums;
using Microsoft.AspNetCore.Authorization;

namespace Delobytes.App.Backend.Identity.Infrastructure.Authorization;

/// <summary>
/// Authorization handler that checks if the user has a required role from JWT claims.
/// </summary>
public class RoleAuthorizationHandler : AuthorizationHandler<RoleRequirement>
{
    /// <inheritdoc/>
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, RoleRequirement requirement)
    {
        string? roleClaim = context.User.FindFirst("role")?.Value;

        if (string.IsNullOrEmpty(roleClaim))
        {
            return Task.CompletedTask;
        }

        if (Enum.TryParse<Role>(roleClaim, out Role userRole))
        {
            // Administrator has access to everything
            if (userRole == Role.Administrator)
            {
                context.Succeed(requirement);
                return Task.CompletedTask;
            }

            // Check if user role matches the required role
            if (requirement.AllowedRoles.Contains(userRole))
            {
                context.Succeed(requirement);
            }
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Requirement that specifies allowed roles.
/// </summary>
public class RoleRequirement : IAuthorizationRequirement
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RoleRequirement"/> class.
    /// </summary>
    /// <param name="allowedRoles">Allowed roles.</param>
    public RoleRequirement(params Role[] allowedRoles)
    {
        AllowedRoles = allowedRoles;
    }

    /// <summary>
    /// Gets the allowed roles.
    /// </summary>
    public Role[] AllowedRoles { get; }
}
