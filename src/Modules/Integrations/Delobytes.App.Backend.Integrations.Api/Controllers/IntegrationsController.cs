using Delobytes.App.Backend.Integrations.Application.Commands.CancelJob;
using Delobytes.App.Backend.Integrations.Application.Commands.CreateConnection;
using Delobytes.App.Backend.Integrations.Application.Commands.DeleteConnection;
using Delobytes.App.Backend.Integrations.Application.Commands.StartJob;
using Delobytes.App.Backend.Integrations.Application.DTOs.Channels;
using Delobytes.App.Backend.Integrations.Application.DTOs.Connections;
using Delobytes.App.Backend.Integrations.Application.DTOs.Jobs;
using Delobytes.App.Backend.Integrations.Application.Extensions;
using Delobytes.App.Backend.Integrations.Application.Queries.GetAvailableChannels;
using Delobytes.App.Backend.Integrations.Application.Queries.GetConnections;
using Delobytes.App.Backend.Integrations.Application.Queries.GetJob;
using Delobytes.App.Backend.Integrations.Application.Queries.GetJobs;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Delobytes.App.Backend.Integrations.Api.Controllers;

[ApiController]
[Route("api/integrations")]
[Authorize]
public class IntegrationsController : ControllerBase
{
    private readonly IMediator _mediator;

    public IntegrationsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Get the available channels.
    /// </summary>
    [HttpGet("channels")]
    public async Task<ActionResult<GetAvailableChannelsResponse>> GetChannels(CancellationToken cancellationToken)
    {
        GetAvailableChannelsResponse response = await _mediator.Send(
            new GetAvailableChannelsQuery(), cancellationToken);

        return Ok(response);
    }

    /// <summary>
    /// Get the current connections.
    /// </summary>
    [HttpGet("connections")]
    public async Task<ActionResult<GetConnectionsResponse>> GetConnections(CancellationToken cancellationToken)
    {
        GetConnectionsResponse response = await _mediator.Send(
            new GetConnectionsQuery(), cancellationToken);

        return Ok(response);
    }

    /// <summary>
    /// Creates a connection.
    /// </summary>
    [HttpPost("connections")]
    public async Task<ActionResult<CreateConnectionResponse>> CreateConnection(
        [FromBody] CreateConnectionRequest request,
        CancellationToken cancellationToken)
    {
        CreateConnectionResponse response = await _mediator.Send(
            new CreateConnectionCommand
            {
                ChannelId = request.ChannelId,
                SystemChannelTemplateCode = request.SystemChannelTemplateCode,
                ApiKey = request.ApiKey,
                ApiSecret = request.ApiSecret,
                Settings = request.Settings,
            },
            cancellationToken);

        return CreatedAtAction(nameof(GetConnections), new { id = response.ConnectionId }, response);
    }

    /// <summary>
    /// Deletes the connection.
    /// </summary>
    [HttpDelete("connections/{id:guid}")]
    public async Task<IActionResult> DeleteConnection(Guid id, CancellationToken cancellationToken)
    {
        await _mediator.Send(new DeleteConnectionCommand { Id = id }, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Returns job runs of any type for the current tenant.
    /// </summary>
    [HttpGet("jobs")]
    [ProducesResponseType(typeof(GetJobsResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<GetJobsResponse>> GetJobs(
        [FromQuery] string? type,
        [FromQuery] Guid? connectionId,
        [FromQuery] JobStatus[]? status,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
    {
        GetJobsResponse response = await _mediator.Send(
            new GetJobsQuery
            {
                Type = type,
                ConnectionId = connectionId,
                Status = status,
                Page = page,
                PageSize = pageSize,
            },
            cancellationToken);

        return Ok(response);
    }

    /// <summary>
    /// Returns a single job run of any type.
    /// </summary>
    [HttpGet("jobs/{id:guid}")]
    [ProducesResponseType(typeof(JobDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<JobDto>> GetJob(Guid id, CancellationToken cancellationToken)
    {
        JobDto response = await _mediator.Send(new GetJobQuery { JobRunId = id }, cancellationToken);
        return Ok(response);
    }

    /// <summary>
    /// Starts a job run of the given type. Only products import is manually startable; orders
    /// import is driven by the scheduler.
    /// </summary>
    /// <remarks>
    /// The Administrator restriction is enforced by the IRequireRole MediatR behaviour on
    /// <see cref="StartJobCommand"/> rather than by an [Authorize(Roles = ...)] attribute: the
    /// JWT pipeline clears the inbound claim map and issues the role as a raw "role" claim, so
    /// the framework's RoleClaimType (ClaimTypes.Role) never matches and the attribute would
    /// reject every caller, administrators included.
    /// </remarks>
    [HttpPost("jobs/{type}")]
    [ProducesResponseType(typeof(StartJobResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<StartJobResponse>> StartJob(
        string type,
        [FromBody] StartJobRequest request,
        CancellationToken cancellationToken)
    {
        if (!JobTypeExtensions.TryFromString(type, out JobType jobType))
        {
            return BadRequest(new
            {
                error = $"Неизвестный тип задачи '{type}'.",
            });
        }

        StartJobResponse response = await _mediator.Send(
            new StartJobCommand
            {
                JobType = jobType,
                ConnectionId = request.ConnectionId,
            },
            cancellationToken);

        return AcceptedAtAction(nameof(GetJob), new { id = response.JobRunId }, response);
    }

    /// <summary>
    /// Requests cancellation of a job run. Job types started by the scheduler are not cancellable.
    /// </summary>
    [HttpDelete("jobs/{id:guid}/cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CancelJob(Guid id, CancellationToken cancellationToken)
    {
        await _mediator.Send(new CancelJobCommand { JobRunId = id }, cancellationToken);
        return NoContent();
    }
}
