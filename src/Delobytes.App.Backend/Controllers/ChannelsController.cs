using Delobytes.App.Backend.Catalog.Application.Commands.Channels.CreateChannel;
using Delobytes.App.Backend.Catalog.Application.Queries.Channels.GetChannels;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Delobytes.App.Backend.Controllers;

/// <summary>
/// Endpoints for managing sales channels (Channel) — independent business entities that
/// hold product costs and analytics data. A channel may optionally have a Connection
/// (see IntegrationsController) attached to it for automated data collection.
/// </summary>
[ApiController]
[Route("api/catalogs/channels")]
[Authorize]
public class ChannelsController : ControllerBase
{
    private readonly IMediator _mediator;

    public ChannelsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<GetChannelsResponse>> GetAll(CancellationToken cancellationToken)
    {
        GetChannelsResponse response = await _mediator.Send(new GetChannelsQuery(), cancellationToken);
        return Ok(response);
    }

    [HttpPost]
    public async Task<ActionResult<CreateChannelResponse>> Create(
        [FromBody] CreateChannelRequest request,
        CancellationToken cancellationToken)
    {
        CreateChannelResponse response = await _mediator.Send(
            new CreateChannelCommand
            {
                Name = request.Name,
                SystemChannelTemplateId = request.SystemChannelTemplateId,
                CustomApiUrl = request.CustomApiUrl,
            },
            cancellationToken);

        return CreatedAtAction(nameof(GetAll), new { id = response.Id }, response);
    }
}

/// <summary>Request body for creating a sales channel.</summary>
public class CreateChannelRequest
{
    public string Name { get; set; } = default!;

    public Guid? SystemChannelTemplateId { get; set; }

    public string? CustomApiUrl { get; set; }
}
