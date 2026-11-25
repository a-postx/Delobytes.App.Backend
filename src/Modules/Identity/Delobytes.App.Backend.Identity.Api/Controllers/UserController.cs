using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Identity.Application.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Delobytes.App.Backend.Identity.Api.Controllers;

/// <summary>
/// Endpoint for retrieving information about the currently authenticated user.
/// </summary>
[ApiController]
[Route("api")]
[Authorize]
public class UserController : TenantApiControllerBase
{
    private readonly IMediator _mediator;

    /// <summary>
    /// Initializes a new instance of the <see cref="UserController"/> class.
    /// </summary>
    /// <param name="mediator">Mediator instance.</param>
    /// <param name="userContext">Current user context.</param>
    /// <param name="tenantContext">Current tenant context.</param>
    public UserController(
        IMediator mediator,
        IUserContext userContext,
        ITenantContext tenantContext)
        : base(userContext, tenantContext)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Returns information about the current user and their active tenant.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Current user info including tenant name.</returns>
    [HttpGet("me")]
    public async Task<ActionResult<GetCurrentUserResponse>> GetMe(CancellationToken cancellationToken)
    {
        Guid userId = CurrentUserId;
        Guid tenantId = CurrentTenantId;

        GetCurrentUserResponse response = await _mediator.Send(
            new GetCurrentUserQuery { UserId = userId, TenantId = tenantId },
            cancellationToken);

        return Ok(response);
    }
}
