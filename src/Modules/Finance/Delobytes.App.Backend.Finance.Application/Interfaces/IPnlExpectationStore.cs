using Delobytes.App.Backend.Finance.Application.ReadModels;
using Delobytes.App.Backend.Finance.Domain.Entities;

namespace Delobytes.App.Backend.Finance.Application.Interfaces;

/// <summary>
/// Persistence port of the expected-document open items. The expectations are written by the
/// projection that rebuilds a line and by the aging pass, never by the read side.
/// </summary>
public interface IPnlExpectationStore
{
    /// <summary>
    /// Loads every expectation of <paramref name="orderLineIds"/>, open and closed.
    /// </summary>
    /// <param name="orderLineIds">Order line identifiers.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The expectations found.</returns>
    public Task<IReadOnlyList<PnlLineExpectation>> LoadByOrderLineIdsAsync(
        IReadOnlyCollection<Guid> orderLineIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// Loads the expectations whose window elapsed and that are still waiting, oldest first.
    /// </summary>
    /// <param name="now">Current instant.</param>
    /// <param name="take">Maximum number of rows to load.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The due expectations.</returns>
    public Task<IReadOnlyList<PnlLineExpectation>> LoadDueAsync(
        DateTimeOffset now,
        int take,
        CancellationToken cancellationToken);

    /// <summary>
    /// Estimates the impact of the missing document for each line: the median delivery fee of the
    /// same catalogue product over the lookback window. Lines without enough comparable history are
    /// absent from the result rather than estimated as zero.
    /// </summary>
    /// <param name="orderLineIds">Lines whose expectations are due.</param>
    /// <param name="lookbackDays">How far back the comparable history is taken from.</param>
    /// <param name="today">Tenant-local today.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Estimates keyed by order line identifier.</returns>
    public Task<IReadOnlyDictionary<Guid, decimal>> EstimateLogisticsImpactAsync(
        IReadOnlyCollection<Guid> orderLineIds,
        int lookbackDays,
        DateOnly today,
        CancellationToken cancellationToken);

    /// <summary>
    /// Lists the tenants that have at least one expectation past its window. System-level: the
    /// aging sweep runs outside a request and discovers its tenants through this call, then does
    /// every write inside a tenant scope.
    /// </summary>
    /// <param name="now">Current instant.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Distinct tenant identifiers with work to do.</returns>
    public Task<IReadOnlyList<Guid>> LoadTenantIdsWithDueAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads one keyset page of the expectation queue.
    /// </summary>
    /// <param name="filter">Filter of the queue.</param>
    /// <param name="take">Page size.</param>
    /// <param name="afterToken">Cursor produced by the previous page.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The page.</returns>
    public Task<PnlExpectationPage> GetPageAsync(
        PnlExpectationFilter filter,
        int take,
        string? afterToken,
        CancellationToken cancellationToken);

    /// <summary>
    /// Closes an overdue expectation with an administrative decision.
    /// </summary>
    /// <param name="expectationId">Expectation identifier.</param>
    /// <param name="reason">Closure reason.</param>
    /// <param name="userId">Administrator who closed it.</param>
    /// <param name="now">Current instant.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The outcome, including the line whose completeness must be recomputed.</returns>
    public Task<PnlExpectationCloseOutcome> CloseWithExceptionAsync(
        Guid expectationId,
        string reason,
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns a closed expectation to the queue so a mistaken decision can be taken back.
    /// </summary>
    /// <param name="expectationId">Expectation identifier.</param>
    /// <param name="now">Current instant.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The outcome, including the line whose completeness must be recomputed.</returns>
    public Task<PnlExpectationCloseOutcome> ReopenAsync(
        Guid expectationId,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    /// <summary>
    /// Stages a new expectation.
    /// </summary>
    /// <param name="expectation">Expectation to add.</param>
    public void Add(PnlLineExpectation expectation);

    /// <summary>
    /// Persists every pending change.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task SaveChangesAsync(CancellationToken cancellationToken);
}
