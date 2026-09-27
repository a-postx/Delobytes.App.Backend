using Delobytes.App.Backend.Integrations.Application.Commands.CancelSyncJob;
using Delobytes.App.Backend.Integrations.Application.Commands.CreateConnection;
using Delobytes.App.Backend.Integrations.Application.Commands.DeleteConnection;
using Delobytes.App.Backend.Integrations.Application.Commands.StartProductsImport;
using Delobytes.App.Backend.Integrations.Application.DTOs.Channels;
using Delobytes.App.Backend.Integrations.Application.DTOs.Connections;
using Delobytes.App.Backend.Integrations.Application.DTOs.SyncJobs;
using Delobytes.App.Backend.Integrations.Application.Queries.GetAvailableChannels;
using Delobytes.App.Backend.Integrations.Application.Queries.GetConnections;
using Delobytes.App.Backend.Integrations.Application.Queries.GetSyncJob;
using Delobytes.App.Backend.Integrations.Application.Queries.GetSyncJobs;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Delobytes.App.Backend.Controllers;

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

    [HttpGet("channels")]
    public async Task<ActionResult<GetAvailableChannelsResponse>> GetChannels(CancellationToken cancellationToken)
    {
        GetAvailableChannelsResponse response = await _mediator.Send(
            new GetAvailableChannelsQuery(), cancellationToken);

        return Ok(response);
    }

    [HttpGet("connections")]
    public async Task<ActionResult<GetConnectionsResponse>> GetConnections(CancellationToken cancellationToken)
    {
        GetConnectionsResponse response = await _mediator.Send(
            new GetConnectionsQuery(), cancellationToken);

        return Ok(response);
    }

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

    [HttpDelete("connections/{id:guid}")]
    public async Task<IActionResult> DeleteConnection(Guid id, CancellationToken cancellationToken)
    {
        await _mediator.Send(new DeleteConnectionCommand { Id = id }, cancellationToken);
        return NoContent();
    }

    [HttpPost("product-imports")]
    [ProducesResponseType(typeof(StartProductsImportResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<StartProductsImportResponse>> StartProductsImport(
        [FromBody] StartProductsImportRequest request,
        CancellationToken cancellationToken)
    {
        StartProductsImportResponse response = await _mediator.Send(
            new StartProductsImportCommand
            {
                ConnectionId = request.ConnectionId,
            },
            cancellationToken);

        return AcceptedAtAction(nameof(GetProductImport), new { id = response.SyncJobId }, response);
    }

    [HttpGet("product-imports")]
    [ProducesResponseType(typeof(GetSyncJobsResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<GetSyncJobsResponse>> GetProductImports(CancellationToken cancellationToken)
    {
        GetSyncJobsResponse response = await _mediator.Send(new GetSyncJobsQuery(), cancellationToken);
        return Ok(response);
    }

    [HttpGet("product-imports/{id:guid}")]
    [ProducesResponseType(typeof(GetSyncJobResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetSyncJobResponse>> GetProductImport(Guid id, CancellationToken cancellationToken)
    {
        GetSyncJobResponse response = await _mediator.Send(
            new GetSyncJobQuery { SyncJobId = id }, cancellationToken);

        return Ok(response);
    }

    [HttpPost("product-imports/{id:guid}/cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CancelProductImport(Guid id, CancellationToken cancellationToken)
    {
        await _mediator.Send(new CancelSyncJobCommand { SyncJobId = id }, cancellationToken);
        return NoContent();
    }
}
