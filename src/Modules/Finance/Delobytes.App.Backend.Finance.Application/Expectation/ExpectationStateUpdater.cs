using Delobytes.App.Backend.Finance.Application.Interfaces;
using Delobytes.App.Backend.Finance.Application.Models;
using Delobytes.App.Backend.Finance.Application.Projection;
using Delobytes.App.Backend.Finance.Domain.Entities;
using Delobytes.App.Backend.Finance.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Finance.Application.Expectation;

/// <summary>
/// Ages the expected-document open items: every expectation whose window elapsed either closes
/// itself as immaterial or moves to the administrator's queue, and the affected lines are
/// recomputed so their completeness reflects the new state.
/// </summary>
/// <remarks>
/// The pass never writes the P&amp;L line directly. It transitions the expectations and then hands
/// the affected order lines to <see cref="PnlLineProjector"/>, which stays the single writer of the
/// line and of its completeness — otherwise the two writers would drift on the next recompute.
/// </remarks>
public class ExpectationStateUpdater
{
    private const int MaxBatchSize = 500;

    private readonly IPnlExpectationStore _expectations;
    private readonly PnlLineProjector _projector;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ExpectationStateUpdater> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExpectationStateUpdater"/> class.
    /// </summary>
    /// <param name="expectations">Expectation persistence port.</param>
    /// <param name="projector">P&amp;L line projector.</param>
    /// <param name="timeProvider">Clock source.</param>
    /// <param name="logger">Logger instance.</param>
    public ExpectationStateUpdater(
        IPnlExpectationStore expectations,
        PnlLineProjector projector,
        TimeProvider timeProvider,
        ILogger<ExpectationStateUpdater> logger)
    {
        _expectations = expectations;
        _projector = projector;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Runs one aging pass for the current tenant.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of expectations the pass examined.</returns>
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();

        IReadOnlyList<PnlLineExpectation> due =
            await _expectations.LoadDueAsync(now, MaxBatchSize, cancellationToken);

        if (due.Count == 0)
        {
            return 0;
        }

        List<Guid> orderLineIds = due
            .Select(e => e.OrderLineId)
            .Distinct()
            .ToList();

        IReadOnlyDictionary<Guid, decimal> estimates = await _expectations.EstimateLogisticsImpactAsync(
            orderLineIds,
            ExpectationPolicy.EstimateLookbackDays,
            DateOnly.FromDateTime(now.UtcDateTime),
            cancellationToken);

        HashSet<Guid> affected = new();
        int closed = 0;
        int overdue = 0;

        foreach (PnlLineExpectation expectation in due)
        {
            // The load already filtered on Expected, but a concurrent pass may have transitioned the
            // same row between the query and this loop; re-checking keeps the transition idempotent.
            if (expectation.State != PnlExpectationState.Expected)
            {
                continue;
            }

            if (!expectation.AmountEstimate.HasValue
                && estimates.TryGetValue(expectation.OrderLineId, out decimal estimate))
            {
                expectation.AmountEstimate = estimate;
            }

            expectation.UpdatedAt = now;

            if (ExpectationPolicy.IsImmaterial(expectation.AmountEstimate, expectation.Currency))
            {
                expectation.State = PnlExpectationState.ClosedWithException;
                expectation.ClosedAt = now;
                expectation.ClosedByUserId = null;
                expectation.ClosureReason = ExpectationPolicy.ImmaterialClosureReason;
                closed++;
            }
            else
            {
                expectation.State = PnlExpectationState.Overdue;
                overdue++;
            }

            affected.Add(expectation.OrderLineId);
        }

        await _expectations.SaveChangesAsync(cancellationToken);

        PnlRecomputeResult recompute = await _projector.RecomputeAsync(affected, cancellationToken);

        _logger.LogInformation(
            "Expectation aging pass finished. Examined={Examined}, ClosedAsImmaterial={Closed}, Overdue={Overdue}, LinesRecomputed={Recomputed}, RecomputeFailed={Failed}",
            due.Count,
            closed,
            overdue,
            recompute.Succeeded,
            recompute.Failed);

        return due.Count;
    }
}
