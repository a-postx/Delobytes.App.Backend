using Delobytes.App.Backend.Sales.Application.Models;
using Delobytes.App.Backend.Sales.Domain.Entities;

namespace Delobytes.App.Backend.Sales.Application.Interfaces;

/// <summary>
/// Persistence port of the orders batch import. Hides the EF Core specifics the scenario must not
/// know about — change tracking, retries of a save that lost a unique/concurrency race, and the
/// tracked-set lookups — behind the operations it actually needs.
/// </summary>
public interface IOrdersImportStore
{
    /// <summary>
    /// Resolves the Catalog channels product ids for the external product ids the batch mentions.
    /// </summary>
    public Task<Dictionary<string, Guid>> LoadChannelProductIdsAsync(
        Guid channelId,
        IReadOnlyCollection<string> externalProductIds,
        CancellationToken cancellationToken);

    /// <summary>Finds an existing order of the channel, with its lines loaded.</summary>
    public Task<Order?> FindOrderAsync(
        Guid channelId,
        string externalOrderId,
        CancellationToken cancellationToken);

    /// <summary>Stages a new order.</summary>
    public void AddOrder(Order order);

    /// <summary>Stages a new order line.</summary>
    public void AddOrderLine(OrderLine line);

    /// <summary>Runs change detection so the property assignments made by the caller become visible.</summary>
    public void DetectChanges();

    /// <summary>
    /// Reports whether the tracked order has a meaningful change — any modified property except the
    /// import bookkeeping timestamps.
    /// </summary>
    public bool HasMeaningfulOrderChanges(Order order);

    /// <summary>
    /// Reports whether any of the order's lines was added or meaningfully modified.
    /// </summary>
    public bool HasMeaningfulLineChanges(Order order);

    /// <summary>
    /// Describes every line of the order together with the two facts the change event needs: whether
    /// the row was added by this import and whether a fact that decides the production cost changed.
    /// Must be called after <see cref="DetectChanges"/> and before the save resets the entry states.
    /// </summary>
    /// <param name="order">Tracked order whose lines are described.</param>
    /// <returns>One entry per line, in the order the lines are held.</returns>
    public IReadOnlyList<OrderLineChange> DescribeLineChanges(Order order);

    /// <summary>
    /// Runs <paramref name="operation"/> and persists its changes, retrying when the save loses a
    /// unique or concurrency race, and detaching whatever the failed attempt staged before retrying.
    /// </summary>
    /// <typeparam name="TResult">Result of <paramref name="operation"/>.</typeparam>
    public Task<TResult> ExecuteWithSaveAsync<TResult>(
        Func<Task<TResult>> operation,
        string externalOrderId,
        CancellationToken cancellationToken);
}
