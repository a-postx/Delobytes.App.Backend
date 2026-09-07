using Delobytes.App.Backend.Integrations.Application.Commands.CreateConnection;
using Delobytes.App.Backend.Integrations.Application.Commands.DeleteConnection;
using Delobytes.App.Backend.Integrations.Application.DTOs.Channels;
using Delobytes.App.Backend.Integrations.Application.DTOs.Connections;
using Delobytes.App.Backend.Integrations.Application.Queries.GetAvailableChannels;
using Delobytes.App.Backend.Integrations.Application.Queries.GetConnections;
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
}
