using Delobytes.App.Backend.Catalog.Application.Commands.ChannelParameterSets.CreateChannelParameterSet;
using Delobytes.App.Backend.Catalog.Application.Queries.ChannelParameterSets.GetActiveChannelParameterSet;
using Delobytes.App.Backend.Catalog.Application.Queries.ChannelParameterSets.GetChannelParameterSets;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Delobytes.App.Backend.Controllers;

/// <summary>
/// Endpoints for managing versioned channel parameters (commission, acquiring, SPP).
/// Each change creates a new version with ValidFrom date; historical snapshots are preserved.
/// </summary>
[ApiController]
[Route("api/catalogs/channels/{channelId:guid}/parameter-sets")]
[Authorize]
public class ChannelParameterSetsController : ControllerBase
{
    private readonly IMediator _mediator;

    public ChannelParameterSetsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Get all parameter set versions for a channel, ordered by ValidFrom descending.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<GetChannelParameterSetsResponse>> GetAll(
        Guid channelId,
        CancellationToken cancellationToken)
    {
        GetChannelParameterSetsResponse response = await _mediator.Send(
            new GetChannelParameterSetsQuery { ChannelId = channelId },
            cancellationToken);

        return Ok(response);
    }

    /// <summary>
    /// Get the currently active parameter set for a channel (ValidFrom &lt;= today, most recent).
    /// </summary>
    [HttpGet("active")]
    public async Task<ActionResult<GetActiveChannelParameterSetResponse>> GetActive(
        Guid channelId,
        CancellationToken cancellationToken)
    {
        GetActiveChannelParameterSetResponse response = await _mediator.Send(
            new GetActiveChannelParameterSetQuery { ChannelId = channelId },
            cancellationToken);

        if (!response.Found)
        {
            return NotFound();
        }

        return Ok(response);
    }

    /// <summary>
    /// Create a new parameter set version. Does not modify existing versions.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<CreateChannelParameterSetResponse>> Create(
        Guid channelId,
        [FromBody] CreateChannelParameterSetRequest request,
        CancellationToken cancellationToken)
    {
        CreateChannelParameterSetResponse response = await _mediator.Send(
            new CreateChannelParameterSetCommand
            {
                ChannelId = channelId,
                ValidFrom = request.ValidFrom,
            },
            cancellationToken);

        if (!response.ChannelFound)
        {
            return NotFound();
        }

        return CreatedAtAction(nameof(GetAll), new { channelId }, response);
    }
}

/// <summary>Request body for creating a new parameter set version.</summary>
public class CreateChannelParameterSetRequest
{
    public DateOnly ValidFrom { get; set; }
}
