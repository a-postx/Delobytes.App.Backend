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
        [FromQuery] Guid? channelProductId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] string? state,
        [FromQuery] string? completeness,
        [FromQuery] int take,
        [FromQuery] string? continuationToken,
        CancellationToken cancellationToken)
    {
        ValidateCompleteness(completeness);

        PnlLinesResponse response = await _mediator.Send(
            new GetPnlLinesQuery
            {
                Filter = new PnlQueryFilter
                {
                    ChannelId = channelId,
                    ProductId = productId,
                    ChannelProductId = channelProductId,
                    From = from,
                    To = to,
                    State = state,
                    Completeness = completeness,
                },
                Take = take <= 0 ? 100 : take,
                ContinuationToken = continuationToken,
            },
            cancellationToken);

        return Ok(response);
    }

    /// <summary>
    /// Returns the summary by product, by channel or with no dimension, and by period. A granularity
    /// of <c>total</c> reports the whole requested range as one period; with a <c>groupBy</c> of
    /// <c>none</c> the whole range collapses into a single row.
    /// </summary>
    [HttpGet("pnl-summary")]
    [ProducesResponseType(typeof(PnlSummaryResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<PnlSummaryResponse>> GetSummary(
        [FromQuery] Guid? channelId,
        [FromQuery] Guid? productId,
        [FromQuery] Guid? channelProductId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] string? granularity,
        [FromQuery] string? groupBy,
        [FromQuery] string? completeness,
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
                        new ValidationFailure(
                            nameof(granularity),
                            "Гранулярность должна быть одной из: day, week, month, total."),
                    });
            }
        }

        PnlSummaryGroupBy parsedGroupBy = PnlSummaryGroupBy.Product;

        if (groupBy != null)
        {
            if (!Enum.TryParse(groupBy, ignoreCase: true, out parsedGroupBy)
                || !Enum.IsDefined(parsedGroupBy))
            {
                throw new ValidationException(
                    new[]
                    {
                        new ValidationFailure(
                            nameof(groupBy),
                            "Группировка должна быть одной из: product, channel, none."),
                    });
            }
        }

        ValidateCompleteness(completeness);

        PnlSummaryResponse response = await _mediator.Send(
            new GetPnlSummaryQuery
            {
                Filter = new PnlQueryFilter
                {
                    ChannelId = channelId,
                    ProductId = productId,
                    ChannelProductId = channelProductId,
                    From = from,
                    To = to,
                    Completeness = completeness,
                },
                Granularity = parsedGranularity,
                GroupBy = parsedGroupBy,
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

    private static void ValidateCompleteness(string? completeness)
    {
        if (PnlCompletenessFilter.TryParse(completeness, out _))
        {
            return;
        }

        throw new ValidationException(
            new[]
            {
                new ValidationFailure(
                    nameof(completeness),
                    $"Полнота должна быть одной из: {string.Join(", ", PnlCompletenessFilter.AcceptedValues)}."),
            });
    }
}
