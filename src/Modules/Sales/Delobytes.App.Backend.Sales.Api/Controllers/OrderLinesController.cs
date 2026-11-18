using Delobytes.App.Backend.Sales.Application.Models;
using Delobytes.App.Backend.Sales.Application.Queries.Orders;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Delobytes.App.Backend.Sales.Api.Controllers;

/// <summary>
/// Operational endpoints over the Sales order lines.
/// </summary>
[ApiController]
[Route("api/sales/order-lines")]
[Authorize]
public class OrderLinesController : ControllerBase
{
    private readonly IMediator _mediator;

    /// <summary>
    /// Initializes a new instance of the <see cref="OrderLinesController"/> class.
    /// </summary>
    /// <param name="mediator">Mediator.</param>
    public OrderLinesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Re-publishes the change facts of one bounded page of order lines. Used to backfill existing
    /// orders and to recover from a lost event; the response carries the continuation token.
    /// </summary>
    /// <param name="request">Filters, continuation token and page size.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The published counters and the next continuation token.</returns>
    [HttpPost("republish")]
    [ProducesResponseType(typeof(RepublishOrderLinesResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<RepublishOrderLinesResponse>> Republish(
        [FromBody] RepublishOrderLinesRequest request,
        CancellationToken cancellationToken)
    {
        RepublishOrderLinesResponse response = await _mediator.Send(
            new RepublishOrderLinesCommand
            {
                ChannelId = request.ChannelId,
                FromDate = request.FromDate,
                ToDate = request.ToDate,
                ContinuationToken = request.ContinuationToken,
                MaxLines = request.MaxLines,
            },
            cancellationToken);

        return Ok(response);
    }
}
