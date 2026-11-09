using Delobytes.App.Backend.Sales.Application.Interfaces;
using Delobytes.App.Backend.Sales.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Delobytes.App.Backend.Sales.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of <see cref="IOrdersImportStore"/>. Owns everything the import scenario
/// must not know about: tracked-set lookups, change tracking and retries of saves that lost a
/// unique or concurrency race.
/// </summary>
public class OrdersImportStore : IOrdersImportStore
{
    /// <summary>
    /// Postgres SQLSTATE for a unique constraint violation. Used to tell a concurrent-insert
    /// conflict (retryable) apart from a real failure (not).
    /// </summary>
    private const string UniqueViolationSqlState = "23505";

    /// <summary>
    /// Upper bound on retry attempts for a single order upsert.
    /// </summary>
    private const int MaxUpsertAttempts = 3;

    private const int UpsertRetryBaseDelayMilliseconds = 100;

    /// <summary>
    /// Depth limit when walking an exception chain. A cycle is not possible in practice; the bound
    /// only keeps a pathological chain from turning into an unbounded loop while handling a failure.
    /// </summary>
    private const int MaxExceptionDepth = 5;

    /// <summary>
    /// Bookkeeping properties excluded from change detection. They describe the import itself
    /// rather than the imported facts, so a re-import carrying identical channel data must still
    /// count as Skipped instead of being reported as an update.
    /// </summary>
    private static readonly HashSet<string> BookkeepingProperties = new HashSet<string>(StringComparer.Ordinal)
    {
        nameof(Order.CreatedAt),
        nameof(Order.UpdatedAt),
        nameof(Order.FirstImportedAt),
        nameof(Order.LastImportedAt),
    };

    private static readonly HashSet<string> LineBookkeepingProperties = new HashSet<string>(StringComparer.Ordinal)
    {
        nameof(OrderLine.CreatedAt),
        nameof(OrderLine.UpdatedAt),
    };

    private readonly SalesDbContext _context;
    private readonly ILogger<OrdersImportStore> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="OrdersImportStore"/> class.
    /// </summary>
    /// <param name="context">Sales database context.</param>
    /// <param name="logger">Logger instance.</param>
    public OrdersImportStore(SalesDbContext context, ILogger<OrdersImportStore> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<Dictionary<string, Guid>> LoadChannelProductIdsAsync(
        Guid channelId,
        IReadOnlyCollection<string> externalProductIds,
        CancellationToken cancellationToken)
    {
        if (externalProductIds.Count == 0)
        {
            return new Dictionary<string, Guid>(StringComparer.Ordinal);
        }

        List<string> ids = externalProductIds.ToList();

        return await _context.ChannelProductRefs
            .AsNoTracking()
            .Where(r => r.ChannelId == channelId && ids.Contains(r.ExternalProductId))
            .ToDictionaryAsync(r => r.ExternalProductId, r => r.ChannelProductId, StringComparer.Ordinal, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<Order?> FindOrderAsync(
        Guid channelId,
        string externalOrderId,
        CancellationToken cancellationToken)
    {
        return _context.Orders
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(
                o => o.ChannelId == channelId && o.ExternalOrderId == externalOrderId,
                cancellationToken);
    }

    /// <inheritdoc/>
    public void AddOrder(Order order)
    {
        _context.Orders.Add(order);
    }

    /// <inheritdoc/>
    public void AddOrderLine(OrderLine line)
    {
        _context.OrderLines.Add(line);
    }

    /// <inheritdoc/>
    public void DetectChanges()
    {
        _context.ChangeTracker.DetectChanges();
    }

    /// <inheritdoc/>
    public bool HasMeaningfulOrderChanges(Order order)
    {
        EntityEntry<Order> entry = _context.Entry(order);

        if (entry.State != EntityState.Modified)
        {
            return false;
        }

        foreach (PropertyEntry property in entry.Properties)
        {
            if (property.IsModified && !BookkeepingProperties.Contains(property.Metadata.Name))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// A line counts as changed when it is newly added, or when an existing one has any modified
    /// property other than its own bookkeeping timestamps. Detecting modified lines matters:
    /// without it an order whose only difference is a line-level fact (a changed price, a changed
    /// channel product link) would be reported as Skipped while its lines were still written.
    /// </summary>
    /// <param name="order">Order whose lines are inspected.</param>
    /// <returns>True when any line was added or meaningfully modified.</returns>
    public bool HasMeaningfulLineChanges(Order order)
    {
        foreach (OrderLine line in order.Lines)
        {
            EntityEntry<OrderLine> lineEntry = _context.Entry(line);

            if (lineEntry.State == EntityState.Added)
            {
                return true;
            }

            if (lineEntry.State != EntityState.Modified)
            {
                continue;
            }

            foreach (PropertyEntry property in lineEntry.Properties)
            {
                if (property.IsModified && !LineBookkeepingProperties.Contains(property.Metadata.Name))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <inheritdoc/>
    public async Task<TResult> ExecuteWithSaveAsync<TResult>(
        Func<Task<TResult>> operation,
        string externalOrderId,
        CancellationToken cancellationToken)
    {
        int attempt = 0;

        while (true)
        {
            attempt++;

            HashSet<object> trackedBefore = _context.ChangeTracker.Entries().Select(e => e.Entity).ToHashSet();

            try
            {
                TResult result = await operation();

                await _context.SaveChangesAsync(cancellationToken);

                return result;
            }
            catch (DbUpdateException ex) when (
                attempt < MaxUpsertAttempts && (IsUniqueViolation(ex) || IsConcurrencyViolation(ex)))
            {
                // No explicit transaction spans the batch, so a failed SaveChanges rolls back only
                // this order's implicit transaction. The change tracker, however, keeps whatever
                // entries were added for it, so they must be detached before the retry, otherwise
                // the retry's SaveChanges would try to flush the same (now stale) entries again.
                DetachAddedSince(trackedBefore);

                _logger.LogWarning(
                    ex,
                    "Concurrent upsert conflict for order ExternalOrderId={ExternalOrderId}, attempt {Attempt}/{MaxAttempts}, retrying...",
                    externalOrderId,
                    attempt,
                    MaxUpsertAttempts);

                await Task.Delay(
                    TimeSpan.FromMilliseconds(UpsertRetryBaseDelayMilliseconds * Math.Pow(2, attempt - 1)),
                    cancellationToken);
            }
            catch (Exception)
            {
                // Same detach as above, but for the path that rethrows: the caller's own catch
                // logs and moves on to the next order, and must not inherit this order's leftovers.
                DetachAddedSince(trackedBefore);
                throw;
            }
        }
    }

    private void DetachAddedSince(HashSet<object> trackedBefore)
    {
        foreach (EntityEntry entry in _context.ChangeTracker.Entries().ToList())
        {
            if (!trackedBefore.Contains(entry.Entity))
            {
                entry.State = EntityState.Detached;
            }
        }
    }

    /// <summary>
    /// Looks for a unique violation anywhere in the exception chain. EF wraps the provider exception,
    /// and a multi-statement batch can nest it deeper, so checking only the first inner exception
    /// would treat a retryable conflict as a permanent failure.
    /// </summary>
    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        int depth = 0;

        for (Exception? current = ex; current != null && depth < MaxExceptionDepth; depth++)
        {
            if (current is PostgresException postgres && postgres.SqlState == UniqueViolationSqlState)
            {
                return true;
            }

            current = current.InnerException;
        }

        return false;
    }

    private static bool IsConcurrencyViolation(DbUpdateException ex)
    {
        return ex is DbUpdateConcurrencyException;
    }
}
