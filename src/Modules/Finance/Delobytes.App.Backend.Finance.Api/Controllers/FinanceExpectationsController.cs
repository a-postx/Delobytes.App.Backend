using Delobytes.App.Backend.Finance.Application.Commands;
using Delobytes.App.Backend.Finance.Application.Models;
using Delobytes.App.Backend.Finance.Application.Queries;
using Delobytes.App.Backend.Finance.Domain.Enums;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Delobytes.App.Backend.Finance.Api.Controllers;

/// <summary>
/// Operational endpoints of the expected-document queue: what the channel owes and has not reported
/// yet, and the administrative decisions that close it.
/// </summary>
[ApiController]
[Route("api/finance/expectations")]
[Authorize]
public class FinanceExpectationsController : ControllerBase
{
    private readonly IMediator _mediator;

    /// <summary>
    /// Initializes a new instance of the <see cref="FinanceExpectationsController"/> class.
    /// </summary>
    public FinanceExpectationsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Returns the queue with its aging buckets.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PnlExpectationQueueResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<PnlExpectationQueueResponse>> GetExpectations(
        [FromQuery] string? state,
        [FromQuery] DateTimeOffset? dueFrom,
        [FromQuery] DateTimeOffset? dueTo,
        [FromQuery] Guid? orderLineId,
        [FromQuery] int take,
        [FromQuery] string? continuationToken,
        CancellationToken cancellationToken)
    {
        PnlExpectationState? parsedState = null;

        if (!string.IsNullOrWhiteSpace(state))
        {
            if (!Enum.TryParse(state, ignoreCase: true, out PnlExpectationState parsed)
                || !Enum.IsDefined(parsed))
            {
                throw new ValidationException(
                    new[]
                    {
                        new ValidationFailure(
                            nameof(state),
                            "Состояние должно быть одним из: expected, overdue, closedByDocument, closedWithException."),
                    });
            }

            parsedState = parsed;
        }

        PnlExpectationQueueResponse response = await _mediator.Send(
            new GetPnlExpectationsQuery
            {
                State = parsedState,
                DueFrom = dueFrom,
                DueTo = dueTo,
                OrderLineId = orderLineId,
                Take = take <= 0 ? 100 : take,
                ContinuationToken = continuationToken,
            },
            cancellationToken);

        return Ok(response);
    }

    /// <summary>
    /// Closes an overdue expectation with an administrative decision.
    /// </summary>
    [HttpPost("{expectationId:guid}/close")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Close(
        Guid expectationId,
        [FromBody] ClosePnlExpectationRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request?.Reason))
        {
            throw new ValidationException(
                new[] { new ValidationFailure(nameof(request.Reason), "Укажите причину закрытия.") });
        }

        PnlExpectationCommandResult result = await _mediator.Send(
            new ClosePnlExpectationCommand
            {
                ExpectationId = expectationId,
                Reason = request.Reason,
            },
            cancellationToken);

        return MapDecision(result, "Ожидание документа не найдено.", "Закрыть можно только просроченное ожидание.");
    }

    /// <summary>
    /// Returns a closed expectation to the queue.
    /// </summary>
    [HttpPost("{expectationId:guid}/reopen")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Reopen(Guid expectationId, CancellationToken cancellationToken)
    {
        PnlExpectationCommandResult result = await _mediator.Send(
            new ReopenPnlExpectationCommand { ExpectationId = expectationId },
            cancellationToken);

        return MapDecision(
            result,
            "Ожидание документа не найдено.",
            "Переоткрыть можно только ожидание, закрытое решением.");
    }

    /// <summary>
    /// Rebuilds the P&amp;L lines of a period so historical rows receive their expectations.
    /// </summary>
    [HttpPost("backfill")]
    [ProducesResponseType(typeof(PnlRecomputeResult), StatusCodes.Status200OK)]
    public async Task<ActionResult<PnlRecomputeResult>> Backfill(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        CancellationToken cancellationToken)
    {
        PnlRecomputeResult result = await _mediator.Send(
            new BackfillPnlCompletenessCommand { From = from, To = to },
            cancellationToken);

        return Ok(result);
    }

    private ActionResult MapDecision(
        PnlExpectationCommandResult result,
        string notFoundMessage,
        string wrongStateMessage)
    {
        if (result.NotFound)
        {
            return NotFound(new { message = notFoundMessage });
        }

        return result.WrongState
            ? Conflict(new { message = wrongStateMessage })
            : NoContent();
    }
}

/// <summary>
/// Body of the close request.
/// </summary>
public sealed class ClosePnlExpectationRequest
{
    /// <summary>Gets or sets the reason recorded on the closure.</summary>
    public string Reason { get; set; } = default!;
}
