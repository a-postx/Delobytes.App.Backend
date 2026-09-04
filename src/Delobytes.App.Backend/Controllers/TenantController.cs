using System.Security.Claims;
using Delobytes.App.Backend.Identity.Application.Commands.AcceptInvitation;
using Delobytes.App.Backend.Identity.Application.Commands.CreateInvitation;
using Delobytes.App.Backend.Identity.Application.Commands.CreateTenant;
using Delobytes.App.Backend.Identity.Application.Commands.RemoveTenantMember;
using Delobytes.App.Backend.Identity.Application.Commands.RevokeInvitation;
using Delobytes.App.Backend.Identity.Application.Commands.SwitchTenant;
using Delobytes.App.Backend.Identity.Application.Commands.UpdateMembershipRole;
using Delobytes.App.Backend.Identity.Application.Commands.UpdateTenantName;
using Delobytes.App.Backend.Identity.Application.Queries.GetTenantMembers;
using Delobytes.App.Backend.Identity.Domain.Enums;
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

    /// <summary>
    /// Switch to another tenant that the user is a member of.
    /// </summary>
    /// <param name="request">Switch tenant request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>New JWT token with updated tenant context.</returns>
    [HttpPost("switch")]
    public async Task<ActionResult<SwitchTenantResponse>> SwitchTenant(
        [FromBody] SwitchTenantRequest request,
        CancellationToken cancellationToken)
    {
        string? userIdClaim = User.FindFirstValue("userId");

        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out Guid userId))
        {
            return Unauthorized();
        }

        SwitchTenantResponse response = await _mediator.Send(
            new SwitchTenantCommand
            {
                UserId = userId,
                TargetTenantId = request.TargetTenantId,
            },
            cancellationToken);

        return Ok(response);
    }

    /// <summary>
    /// Create a new tenant for the authenticated user.
    /// Only available for users with Administrator role in their current tenant.
    /// Does not switch to the new tenant automatically.
    /// </summary>
    /// <param name="request">Create tenant request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Created tenant information without JWT token.</returns>
    [HttpPost("create")]
    public async Task<ActionResult<CreateTenantForUserResponse>> CreateTenantForUser(
        [FromBody] CreateTenantForUserRequest request,
        CancellationToken cancellationToken)
    {
        string? userIdClaim = User.FindFirstValue("userId");

        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out Guid userId))
        {
            return Unauthorized();
        }

        string? tenantIdClaim = User.FindFirstValue("tenantId");
        Guid? currentTenantId = null;

        if (!string.IsNullOrEmpty(tenantIdClaim) && Guid.TryParse(tenantIdClaim, out Guid parsedTenantId))
        {
            currentTenantId = parsedTenantId;
        }

        CreateTenantResponse response = await _mediator.Send(
            new CreateTenantCommand
            {
                UserId = userId,
                TenantName = request.TenantName,
                CurrentTenantId = currentTenantId,
            },
            cancellationToken);

        return Ok(new CreateTenantForUserResponse
        {
            TenantId = response.TenantId,
            TenantName = request.TenantName,
        });
    }

    /// <summary>
    /// Creates an invitation to join the current tenant.
    /// </summary>
    /// <param name="request">Create invitation request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Created invitation information.</returns>
    [HttpPost("invitations")]
    public async Task<ActionResult<CreateInvitationResponse>> CreateInvitation(
        [FromBody] CreateInvitationRequestDto request,
        CancellationToken cancellationToken)
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

        CreateInvitationResponse response = await _mediator.Send(
            new CreateInvitationCommand
            {
                TenantId = tenantId,
                Email = request.Email,
                Role = request.Role,
                InvitedByUserId = userId,
            },
            cancellationToken);

        return Ok(response);
    }

    /// <summary>
    /// Accepts an invitation to join a tenant.
    /// </summary>
    /// <param name="request">Accept invitation request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Tenant information and new JWT token.</returns>
    [HttpPost("invitations/accept")]
    public async Task<ActionResult<AcceptInvitationResponse>> AcceptInvitation(
        [FromBody] AcceptInvitationRequestDto request,
        CancellationToken cancellationToken)
    {
        string? userIdClaim = User.FindFirstValue("userId");

        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out Guid userId))
        {
            return Unauthorized();
        }

        AcceptInvitationResponse response = await _mediator.Send(
            new AcceptInvitationCommand
            {
                Token = request.Token,
                UserId = userId,
            },
            cancellationToken);

        return Ok(response);
    }

    /// <summary>
    /// Revokes an invitation.
    /// </summary>
    /// <param name="invitationId">Invitation identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success status.</returns>
    [HttpDelete("invitations/{invitationId}")]
    public async Task<ActionResult<RevokeInvitationResponse>> RevokeInvitation(
        Guid invitationId,
        CancellationToken cancellationToken)
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

        RevokeInvitationResponse response = await _mediator.Send(
            new RevokeInvitationCommand
            {
                InvitationId = invitationId,
                TenantId = tenantId,
                RevokedByUserId = userId,
            },
            cancellationToken);

        return Ok(response);
    }

    /// <summary>
    /// Returns all members and pending invitations for the current tenant.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of members and pending invitations.</returns>
    [HttpGet("members")]
    public async Task<ActionResult<GetTenantMembersResponse>> GetTenantMembers(CancellationToken cancellationToken)
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

        GetTenantMembersResponse response = await _mediator.Send(
            new GetTenantMembersQuery
            {
                TenantId = tenantId,
                RequestedByUserId = userId,
            },
            cancellationToken);

        return Ok(response);
    }

    /// <summary>
    /// Updates a member's role within the current tenant.
    /// </summary>
    /// <param name="userId">User identifier.</param>
    /// <param name="request">Update role request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Updated membership information.</returns>
    [HttpPatch("members/{userId}/role")]
    public async Task<ActionResult<UpdateMembershipRoleResponse>> UpdateMemberRole(
        Guid userId,
        [FromBody] UpdateMemberRoleRequestDto request,
        CancellationToken cancellationToken)
    {
        string? updaterIdClaim = User.FindFirstValue("userId");
        string? tenantIdClaim = User.FindFirstValue("tenantId");

        if (string.IsNullOrEmpty(updaterIdClaim) || !Guid.TryParse(updaterIdClaim, out Guid updaterId))
        {
            return Unauthorized();
        }

        if (string.IsNullOrEmpty(tenantIdClaim) || !Guid.TryParse(tenantIdClaim, out Guid tenantId))
        {
            return Unauthorized();
        }

        UpdateMembershipRoleResponse response = await _mediator.Send(
            new UpdateMembershipRoleCommand
            {
                TenantId = tenantId,
                TargetUserId = userId,
                NewRole = request.Role,
                UpdatedByUserId = updaterId,
            },
            cancellationToken);

        return Ok(response);
    }

    /// <summary>
    /// Removes a member from the current tenant.
    /// </summary>
    /// <param name="userId">User identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success status.</returns>
    [HttpDelete("members/{userId}")]
    public async Task<ActionResult<RemoveTenantMemberResponse>> RemoveMember(
        Guid userId,
        CancellationToken cancellationToken)
    {
        string? removerIdClaim = User.FindFirstValue("userId");
        string? tenantIdClaim = User.FindFirstValue("tenantId");

        if (string.IsNullOrEmpty(removerIdClaim) || !Guid.TryParse(removerIdClaim, out Guid removerId))
        {
            return Unauthorized();
        }

        if (string.IsNullOrEmpty(tenantIdClaim) || !Guid.TryParse(tenantIdClaim, out Guid tenantId))
        {
            return Unauthorized();
        }

        RemoveTenantMemberResponse response = await _mediator.Send(
            new RemoveTenantMemberCommand
            {
                TenantId = tenantId,
                TargetUserId = userId,
                RemovedByUserId = removerId,
            },
            cancellationToken);

        return Ok(response);
    }
}

/// <summary>
/// Request model for creating an invitation.
/// </summary>
public class CreateInvitationRequestDto
{
    /// <summary>
    /// Gets or sets the email address of the invitee.
    /// </summary>
    public string Email { get; set; } = default!;

    /// <summary>
    /// Gets or sets the role to assign.
    /// </summary>
    public Role Role { get; set; }
}

/// <summary>
/// Request model for accepting an invitation.
/// </summary>
public class AcceptInvitationRequestDto
{
    /// <summary>
    /// Gets or sets the invitation token.
    /// </summary>
    public string Token { get; set; } = default!;
}

/// <summary>
/// Request model for updating member role.
/// </summary>
public class UpdateMemberRoleRequestDto
{
    /// <summary>
    /// Gets or sets the new role.
    /// </summary>
    public Role Role { get; set; }
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

/// <summary>
/// Request model for switching tenant.
/// </summary>
public class SwitchTenantRequest
{
    /// <summary>
    /// Gets or sets the target tenant identifier.
    /// </summary>
    public Guid TargetTenantId { get; set; }
}

/// <summary>
/// Request model for creating a new tenant for authenticated user.
/// </summary>
public class CreateTenantForUserRequest
{
    /// <summary>
    /// Gets or sets the tenant name.
    /// </summary>
    public string TenantName { get; set; } = default!;
}

/// <summary>
/// Response model for creating a new tenant for authenticated user.
/// </summary>
public class CreateTenantForUserResponse
{
    /// <summary>
    /// Gets or sets the created tenant identifier.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Gets or sets the tenant name.
    /// </summary>
    public string TenantName { get; set; } = default!;
}
