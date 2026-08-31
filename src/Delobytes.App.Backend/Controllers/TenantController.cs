using System.Security.Claims;
using Delobytes.App.Backend.Identity.Application.Commands.UpdateTenantName;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Delobytes.App.Backend.Controllers;

/// <summary>
/// Endpoint for tenant management operations.
/// </summary>
[ApiController]
[Route("api/tenant")]
[Authorize]
public class TenantController : ControllerBase
{
    private readonly IMediator _mediator;

    /// <summary>
    /// Initializes a new instance of the <see cref="TenantController"/> class.
    /// </summary>
    /// <param name="mediator">Mediator instance.</param>
    public TenantController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Updates the name of the current tenant.
    /// </summary>
    /// <param name="request">Update tenant name request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Updated tenant information.</returns>
    [HttpPatch("name")]
    public async Task<ActionResult<UpdateTenantNameResponse>> UpdateTenantName(
        [FromBody] UpdateTenantNameRequest request,
        CancellationToken cancellationToken)
    {
        string? tenantIdClaim = User.FindFirstValue("tenantId");

        if (string.IsNullOrEmpty(tenantIdClaim) || !Guid.TryParse(tenantIdClaim, out Guid tenantId))
        {
            return Unauthorized();
        }

        UpdateTenantNameResponse response = await _mediator.Send(
            new UpdateTenantNameCommand
            {
                TenantId = tenantId,
                Name = request.Name,
            },
            cancellationToken);

        return Ok(response);
    }
}

/// <summary>
/// Request model for updating tenant name.
/// </summary>
public class UpdateTenantNameRequest
{
    /// <summary>
    /// Gets or sets the new tenant name.
    /// </summary>
    public string Name { get; set; } = default!;
}
