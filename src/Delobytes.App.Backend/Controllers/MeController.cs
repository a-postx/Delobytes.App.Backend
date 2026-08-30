using System.Security.Claims;
using Delobytes.App.Backend.Identity.Application.Queries.GetCurrentUser;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Delobytes.App.Backend.Controllers;

/// <summary>
/// Endpoint for retrieving information about the currently authenticated user.
/// </summary>
[ApiController]
[Route("api")]
[Authorize]
public class MeController : ControllerBase
{
    private readonly IMediator _mediator;

    /// <summary>
    /// Initializes a new instance of the <see cref="MeController"/> class.
    /// </summary>
    /// <param name="mediator">Mediator instance.</param>
    public MeController(IMediator mediator)
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
        string? userIdClaim = User.FindFirstValue("userId");
        string? tenantIdClaim = User.FindFirstValue("tenantId");

        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out Guid userId))
        {
            return Unauthorized();
        }

        if (string.IsNullOrEmpty(tenantIdClaim) || !Guid.TryParse(tenantIdClaim, out Guid tenantId))
        {
            return Unauthorized();
        }

        GetCurrentUserResponse response = await _mediator.Send(
            new GetCurrentUserQuery { UserId = userId, TenantId = tenantId },
            cancellationToken);

        return Ok(response);
    }
}
