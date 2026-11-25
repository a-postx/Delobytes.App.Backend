using Delobytes.App.Backend.Contracts.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Delobytes.App.Backend.Identity.Api.Controllers;

/// <summary>
/// Base class for controllers that act on behalf of the current user and tenant.
/// Claims are resolved once by <see cref="IUserContext"/> and <see cref="ITenantContext"/>;
/// a missing tenant is reported as HTTP 401 by the global exception middleware.
/// </summary>
public abstract class TenantApiControllerBase : ControllerBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TenantApiControllerBase"/> class.
    /// </summary>
    /// <param name="userContext">Current user context.</param>
    /// <param name="tenantContext">Current tenant context.</param>
    protected TenantApiControllerBase(IUserContext userContext, ITenantContext tenantContext)
    {
        UserContext = userContext;
        TenantContext = tenantContext;
    }

    /// <summary>
    /// Gets the current user context.
    /// </summary>
    protected IUserContext UserContext { get; }

    /// <summary>
    /// Gets the current tenant context.
    /// </summary>
    protected ITenantContext TenantContext { get; }

    /// <summary>
    /// Gets the current user identifier. The JWT pipeline rejects tokens without a valid "sub".
    /// </summary>
    /// <exception cref="UnauthorizedAccessException">The request has no authenticated user.</exception>
    protected Guid CurrentUserId => UserContext.UserId
        ?? throw new UnauthorizedAccessException("Пользователь не аутентифицирован.");

    /// <summary>
    /// Gets the tenant of the current token.
    /// </summary>
    /// <exception cref="UnauthorizedAccessException">The token carries no valid tenant claim.</exception>
    protected Guid CurrentTenantId => TenantContext.TenantId
        ?? throw new UnauthorizedAccessException("Пользователь не аутентифицирован.");

    /// <summary>
    /// Gets the tenant of the current token, or null for pre-tenant flows such as tenant switch or creation.
    /// </summary>
    protected Guid? CurrentTenantIdOrNull => TenantContext.TenantId;
}
