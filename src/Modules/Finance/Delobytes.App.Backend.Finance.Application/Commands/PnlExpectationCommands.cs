using Delobytes.App.Backend.Contracts.Authorization;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Finance.Application.Interfaces;
using Delobytes.App.Backend.Finance.Application.Models;
using Delobytes.App.Backend.Finance.Application.Projection;
using Delobytes.App.Backend.Finance.Application.ReadModels;
using MediatR;

namespace Delobytes.App.Backend.Finance.Application.Commands;

/// <summary>
/// Outcome of a decision on an expectation, in a shape the API can map to a status code.
/// </summary>
public sealed class PnlExpectationCommandResult
{
    /// <summary>Gets a value indicating whether the expectation does not exist for the tenant.</summary>
    public bool NotFound { get; init; }

    /// <summary>Gets a value indicating whether the expectation is not in a closable state.</summary>
    public bool WrongState { get; init; }

    /// <summary>Gets the order line whose completeness was recomputed, when the decision applied.</summary>
    public Guid? OrderLineId { get; init; }
}

/// <summary>
/// Closes an overdue expectation with an administrative decision. The line becomes final with a
/// recorded exception instead of waiting forever.
/// </summary>
public sealed class ClosePnlExpectationCommand : IRequest<PnlExpectationCommandResult>, IRequireRole
{
    /// <summary>Gets or sets the expectation to close.</summary>
    public Guid ExpectationId { get; set; }

    /// <summary>Gets or sets the reason recorded on the closure.</summary>
    public string Reason { get; set; } = default!;

    /// <inheritdoc/>
    public Role[] AllowedRoles => new[] { Role.Administrator };
}

/// <summary>Handles <see cref="ClosePnlExpectationCommand"/>.</summary>
public sealed class ClosePnlExpectationCommandHandler
    : IRequestHandler<ClosePnlExpectationCommand, PnlExpectationCommandResult>
{
    private readonly IPnlExpectationStore _expectations;
    private readonly PnlLineProjector _projector;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;

    public ClosePnlExpectationCommandHandler(
        IPnlExpectationStore expectations,
        PnlLineProjector projector,
        IUserContext userContext,
        TimeProvider timeProvider)
    {
        _expectations = expectations;
        _projector = projector;
        _userContext = userContext;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc/>
    public async Task<PnlExpectationCommandResult> Handle(
        ClosePnlExpectationCommand request,
        CancellationToken cancellationToken)
    {
        Guid userId = _userContext.UserId
            ?? throw new UnauthorizedAccessException("Пользователь не аутентифицирован.");

        PnlExpectationCloseOutcome outcome = await _expectations.CloseWithExceptionAsync(
            request.ExpectationId,
            request.Reason,
            userId,
            _timeProvider.GetUtcNow(),
            cancellationToken);

        return await PnlExpectationDecision.ApplyAsync(outcome, _projector, cancellationToken);
    }
}

/// <summary>
/// Returns a closed expectation to the queue, so a mistaken decision can be taken back without
/// editing history.
/// </summary>
public sealed class ReopenPnlExpectationCommand : IRequest<PnlExpectationCommandResult>, IRequireRole
{
    /// <summary>Gets or sets the expectation to reopen.</summary>
    public Guid ExpectationId { get; set; }

    /// <inheritdoc/>
    public Role[] AllowedRoles => new[] { Role.Administrator };
}

/// <summary>Handles <see cref="ReopenPnlExpectationCommand"/>.</summary>
public sealed class ReopenPnlExpectationCommandHandler
    : IRequestHandler<ReopenPnlExpectationCommand, PnlExpectationCommandResult>
{
    private readonly IPnlExpectationStore _expectations;
    private readonly PnlLineProjector _projector;
    private readonly TimeProvider _timeProvider;

    public ReopenPnlExpectationCommandHandler(
        IPnlExpectationStore expectations,
        PnlLineProjector projector,
        TimeProvider timeProvider)
    {
        _expectations = expectations;
        _projector = projector;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc/>
    public async Task<PnlExpectationCommandResult> Handle(
        ReopenPnlExpectationCommand request,
        CancellationToken cancellationToken)
    {
        PnlExpectationCloseOutcome outcome = await _expectations.ReopenAsync(
            request.ExpectationId,
            _timeProvider.GetUtcNow(),
            cancellationToken);

        return await PnlExpectationDecision.ApplyAsync(outcome, _projector, cancellationToken);
    }
}

/// <summary>
/// Rebuilds the P&amp;L lines of a period so existing rows receive expectations and completeness.
/// This is the only way the feature reaches history: a re-import of the same channel report instance
/// is rejected as a duplicate, so the lines would otherwise keep their pre-feature state forever.
/// </summary>
public sealed class BackfillPnlCompletenessCommand : IRequest<PnlRecomputeResult>, IRequireRole
{
    /// <summary>Gets or sets the first sale date, inclusive.</summary>
    public DateOnly From { get; set; }

    /// <summary>Gets or sets the last sale date, inclusive.</summary>
    public DateOnly To { get; set; }

    /// <inheritdoc/>
    public Role[] AllowedRoles => new[] { Role.Administrator };
}

/// <summary>Handles <see cref="BackfillPnlCompletenessCommand"/>.</summary>
public sealed class BackfillPnlCompletenessCommandHandler
    : IRequestHandler<BackfillPnlCompletenessCommand, PnlRecomputeResult>
{
    private readonly IPnlLineStore _lines;
    private readonly PnlLineProjector _projector;

    public BackfillPnlCompletenessCommandHandler(IPnlLineStore lines, PnlLineProjector projector)
    {
        _lines = lines;
        _projector = projector;
    }

    /// <inheritdoc/>
    public async Task<PnlRecomputeResult> Handle(
        BackfillPnlCompletenessCommand request,
        CancellationToken cancellationToken)
    {
        if (request.From > request.To)
        {
            throw new ArgumentException("The period start must not be after its end.", nameof(request));
        }

        IReadOnlyList<Guid> orderLineIds = await _lines.LoadOrderLineIdsAsync(
            request.From,
            request.To,
            cancellationToken);

        if (orderLineIds.Count == 0)
        {
            return new PnlRecomputeResult(0, 0, Array.Empty<Guid>());
        }

        return await _projector.RecomputeAsync(orderLineIds, cancellationToken);
    }
}

/// <summary>
/// Shared mapping of a decision outcome: a recompute of the affected line follows an applied change,
/// so the line's completeness never lags behind its expectations.
/// </summary>
internal static class PnlExpectationDecision
{
    public static async Task<PnlExpectationCommandResult> ApplyAsync(
        PnlExpectationCloseOutcome outcome,
        PnlLineProjector projector,
        CancellationToken cancellationToken)
    {
        if (outcome.Status == PnlExpectationCloseStatus.Applied && outcome.OrderLineId.HasValue)
        {
            await projector.RecomputeAsync(new[] { outcome.OrderLineId.Value }, cancellationToken);
        }

        return new PnlExpectationCommandResult
        {
            NotFound = outcome.Status == PnlExpectationCloseStatus.NotFound,
            WrongState = outcome.Status == PnlExpectationCloseStatus.WrongState,
            OrderLineId = outcome.OrderLineId,
        };
    }
}
