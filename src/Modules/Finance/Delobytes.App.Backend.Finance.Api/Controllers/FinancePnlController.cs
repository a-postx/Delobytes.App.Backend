using Delobytes.App.Backend.Finance.Application.Queries;
using Delobytes.App.Backend.Finance.Application.ReadModels;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Delobytes.App.Backend.Finance.Api.Controllers;

/// <summary>
/// Read endpoints of the P&amp;L model.
/// </summary>
[ApiController]
[Route("api/finance")]
[Authorize]
public class FinancePnlController : ControllerBase
{
    private readonly IMediator _mediator;

    /// <summary>
    /// Initializes a new instance of the <see cref="FinancePnlController"/> class.
    /// </summary>
    public FinancePnlController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Returns P&amp;L lines with filters and keyset paging.
    /// </summary>
    [HttpGet("pnl-lines")]
    [ProducesResponseType(typeof(PnlLinesResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<PnlLinesResponse>> GetLines(
        [FromQuery] Guid? channelId,
        [FromQuery] Guid? productId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] string? state,
        [FromQuery] int take,
        [FromQuery] string? continuationToken,
        CancellationToken cancellationToken)
    {
        PnlLinesResponse response = await _mediator.Send(
            new GetPnlLinesQuery
            {
                Filter = new PnlQueryFilter
                {
                    ChannelId = channelId,
                    ProductId = productId,
                    From = from,
                    To = to,
                    State = state,
                },
                Take = take <= 0 ? 100 : take,
                ContinuationToken = continuationToken,
            },
            cancellationToken);

        return Ok(response);
    }

    /// <summary>
    /// Returns the summary by product and period.
    /// </summary>
    [HttpGet("pnl-summary")]
    [ProducesResponseType(typeof(PnlSummaryResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<PnlSummaryResponse>> GetSummary(
        [FromQuery] Guid? channelId,
        [FromQuery] Guid? productId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] string? granularity,
        CancellationToken cancellationToken)
    {
        PnlPeriodGranularity parsedGranularity = PnlPeriodGranularity.Day;

        if (granularity != null)
        {
            if (!Enum.TryParse(granularity, ignoreCase: true, out parsedGranularity)
                || !Enum.IsDefined(parsedGranularity))
            {
                throw new ValidationException(
                    new[]
                    {
                        new ValidationFailure(nameof(granularity), "Гранулярность должна быть одной из: day, week, month."),
                    });
            }
        }

        PnlSummaryResponse response = await _mediator.Send(
            new GetPnlSummaryQuery
            {
                Filter = new PnlQueryFilter
                {
                    ChannelId = channelId,
                    ProductId = productId,
                    From = from,
                    To = to,
                },
                Granularity = parsedGranularity,
            },
            cancellationToken);

        return Ok(response);
    }

    /// <summary>
    /// Returns the period diagnostics: unattributed journal entries, ambiguous attributions and the
    /// aggregate control difference.
    /// </summary>
    [HttpGet("diagnostics")]
    [ProducesResponseType(typeof(PnlDiagnosticsResult), StatusCodes.Status200OK)]
    public async Task<ActionResult<PnlDiagnosticsResult>> GetDiagnostics(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        CancellationToken cancellationToken)
    {
        PnlDiagnosticsResult response = await _mediator.Send(
            new GetPnlDiagnosticsQuery { From = from, To = to },
            cancellationToken);

        return Ok(response);
    }
}
