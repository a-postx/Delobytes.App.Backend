using Delobytes.App.Backend.Sales.Application.Queries.Orders.GetOrdersChart;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Delobytes.App.Backend.Controllers;

/// <summary>
/// Read-only sales reporting endpoints.
/// </summary>
[ApiController]
[Route("api/sales/orders")]
[Authorize]
public class SalesOrdersController : ControllerBase
{
    private readonly IMediator _mediator;

    /// <summary>
    /// Initializes a new instance of the <see cref="SalesOrdersController"/> class.
    /// </summary>
    /// <param name="mediator">Mediator.</param>
    public SalesOrdersController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Returns the ordered units and ordered amount series in the tenant currency,
    /// bucketed by day, week or month. Dates are tenant-local calendar dates and
    /// inclusive; from defaults to the last 7 days ending today, to defaults to today, and
    /// granularity defaults to an adaptive choice by range length.
    /// </summary>
    /// <param name="from">First day of the period, tenant-local (inclusive).</param>
    /// <param name="to">Last day of the period, tenant-local (inclusive).</param>
    /// <param name="granularity">Bucket size: day, week or month. Omitted means adaptive.</param>
    /// <param name="channelId">Optional sales channel filter. Omitted means all channels.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The chart series together with the resolved period, granularity, currency and channel.</returns>
    [HttpGet("chart")]
    [ProducesResponseType(typeof(GetOrdersChartResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<GetOrdersChartResponse>> GetChart(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] string? granularity,
        [FromQuery] Guid? channelId,
        CancellationToken cancellationToken)
    {
        // Bound as a string: an enum-typed parameter would be rejected by model binding with a 400
        // before the handler runs, and the contract requires a 422 naming "granularity".
        ChartGranularity? parsedGranularity = null;

        if (granularity != null)
        {
            if (!Enum.TryParse(granularity, ignoreCase: true, out ChartGranularity value) || !Enum.IsDefined(value))
            {
                throw new ValidationException(
                    new[]
                    {
                        new ValidationFailure(nameof(granularity), "Гранулярность должна быть одной из: day, week, month."),
                    });
            }

            parsedGranularity = value;
        }

        GetOrdersChartResponse response = await _mediator.Send(
            new GetOrdersChartQuery
            {
                From = from,
                To = to,
                Granularity = parsedGranularity,
                ChannelId = channelId,
            },
            cancellationToken);

        return Ok(response);
    }
}
