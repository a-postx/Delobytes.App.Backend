using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using Delobytes.App.Backend.Integrations.Contracts.Models;
using Delobytes.App.Backend.Sales.Domain.Entities;
using Delobytes.App.Backend.Sales.Domain.Enums;
using Delobytes.App.Backend.Sales.Infrastructure.Persistence;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Delobytes.App.Backend.Sales.Infrastructure.Messaging.Consumers;

/// <summary>
/// Consumer for persisting imported order batches into the Sales module. Upserts Order by
/// (TenantId, ChannelId, ExternalOrderId) and OrderLine by (TenantId, OrderId, LineKey),
/// resolves ChannelProductId from the local ChannelProductRef projection, and reports the
/// outcome back to Integrations via OrdersBatchImportCompletedEvent.
/// </summary>
public class OrdersBatchImportRequestedConsumer
{
    /// <summary>
    /// Postgres SQLSTATE for a unique constraint violation. Used to tell a concurrent-insert
    /// conflict (retryable) apart from a real failure (not).
    /// </summary>
    private const string UniqueViolationSqlState = "23505";

    /// <summary>
    /// Upper bound on retry attempts for a single order upsert. Defense-in-depth only: with
    /// sequential FBO/FBS execution enforced at the scheduler, a concurrent conflict on the same
    /// order is rare.
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
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<OrdersBatchImportRequestedConsumer> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="OrdersBatchImportRequestedConsumer"/> class.
    /// </summary>
    /// <param name="context">Sales database context.</param>
    /// <param name="publishEndpoint">MassTransit publish endpoint.</param>
    /// <param name="tenantContext">Tenant context, used for diagnostics.</param>
    /// <param name="logger">Logger instance.</param>
    public OrdersBatchImportRequestedConsumer(
        SalesDbContext context,
        IPublishEndpoint publishEndpoint,
        ITenantContext tenantContext,
        ILogger<OrdersBatchImportRequestedConsumer> logger)
    {
        _context = context;
        _publishEndpoint = publishEndpoint;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    /// <summary>
    /// Processes the OrdersBatchImportRequestedEvent message.
    /// </summary>
    /// <param name="message">Event message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ProcessAsync(OrdersBatchImportRequestedEvent message, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "OrdersBatchImportRequestedConsumer started processing batch. JobRunId={JobRunId}, BatchId={BatchId}, ConnectionId={ConnectionId}, ChannelId={ChannelId}, TenantId={TenantId}, OrdersCount={OrdersCount}",
            message.JobRunId,
            message.BatchId,
            message.ConnectionId,
            message.ChannelId,
            _tenantContext.TenantId,
            message.Orders.Count);

        int created = 0;
        int updated = 0;
        int skipped = 0;
        int failed = 0;

        // Loaded once for the whole batch: a single round trip resolving every line's
        // ExternalProductId is cheaper than one query per line.
        Dictionary<string, Guid> channelProductIdsByExternalProductId = await LoadChannelProductIdsAsync(
            message.ChannelId, message.Orders, cancellationToken);

        foreach (ChannelOrderSnapshot orderSnapshot in message.Orders)
        {
            try
            {
                OrderUpsertStatus status = await UpsertOrderWithRetryAsync(
                    message.ChannelId,
                    message.ConnectionId,
                    orderSnapshot,
                    channelProductIdsByExternalProductId,
                    cancellationToken);

                switch (status)
                {
                    case OrderUpsertStatus.Created:
                        created++;
                        break;
                    case OrderUpsertStatus.Updated:
                        updated++;
                        break;
                    case OrderUpsertStatus.Skipped:
                        skipped++;
                        break;
                }
            }
            catch (Exception ex)
            {
                failed++;
                _logger.LogError(
                    ex,
                    "Failed to import order ExternalOrderId={ExternalOrderId} for JobRunId={JobRunId}",
                    orderSnapshot.ExternalOrderId,
                    message.JobRunId);
            }
        }

        OrdersBatchImportCompletedEvent completedEvent = new OrdersBatchImportCompletedEvent
        {
            JobRunId = message.JobRunId,
            BatchId = message.BatchId,
            Created = created,
            Updated = updated,
            Skipped = skipped,
            Failed = failed,
        };

        await _publishEndpoint.Publish(completedEvent, cancellationToken);

        _logger.LogInformation(
            "OrdersBatchImportRequestedConsumer completed batch. JobRunId={JobRunId}, BatchId={BatchId}, Created={Created}, Updated={Updated}, Skipped={Skipped}, Failed={Failed}",
            message.JobRunId,
            message.BatchId,
            created,
            updated,
            skipped,
            failed);
    }

    /// <summary>
    /// Runs <see cref="UpsertOrderAsync"/> and saves it, retrying with exponential backoff when the
    /// save fails on a concurrent-insert conflict. Defense-in-depth: with FBO and FBS execution
    /// serialized at the scheduler, two writers racing on the same order is expected to be rare, not
    /// the common case this retry is designed to absorb silently.
    /// </summary>
    private async Task<OrderUpsertStatus> UpsertOrderWithRetryAsync(
        Guid channelId,
        Guid connectionId,
        ChannelOrderSnapshot orderSnapshot,
        Dictionary<string, Guid> channelProductIdsByExternalProductId,
        CancellationToken cancellationToken)
    {
        int attempt = 0;

        while (true)
        {
            attempt++;

            HashSet<object> trackedBefore = _context.ChangeTracker.Entries().Select(e => e.Entity).ToHashSet();

            try
            {
                OrderUpsertStatus status = await UpsertOrderAsync(
                    channelId,
                    connectionId,
                    orderSnapshot,
                    channelProductIdsByExternalProductId,
                    cancellationToken);

                await _context.SaveChangesAsync(cancellationToken);

                return status;
            }
            catch (DbUpdateException ex) when (
                attempt < MaxUpsertAttempts && (IsUniqueViolation(ex) || IsConcurrencyViolation(ex)))
            {
                // No explicit transaction spans the batch, so a failed SaveChanges rolls back only
                // this order's implicit transaction. The change tracker, however, keeps whatever
                // entries were added for it, so they must be detached before the retry, otherwise
                // the retry's SaveChanges would try to flush the same (now stale) entries again.
                foreach (EntityEntry entry in _context.ChangeTracker.Entries().ToList())
                {
                    if (!trackedBefore.Contains(entry.Entity))
                    {
                        entry.State = EntityState.Detached;
                    }
                }

                _logger.LogWarning(
                    ex,
                    "Concurrent upsert conflict for order ExternalOrderId={ExternalOrderId}, attempt {Attempt}/{MaxAttempts}, retrying...",
                    orderSnapshot.ExternalOrderId,
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
                foreach (EntityEntry entry in _context.ChangeTracker.Entries().ToList())
                {
                    if (!trackedBefore.Contains(entry.Entity))
                    {
                        entry.State = EntityState.Detached;
                    }
                }

                throw;
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

    private async Task<Dictionary<string, Guid>> LoadChannelProductIdsAsync(
        Guid channelId,
        List<ChannelOrderSnapshot> orders,
        CancellationToken cancellationToken)
    {
        List<string> externalProductIds = orders
            .SelectMany(o => o.Lines)
            .Select(l => l.ExternalProductId)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (externalProductIds.Count == 0)
        {
            return new Dictionary<string, Guid>(StringComparer.Ordinal);
        }

        return await _context.ChannelProductRefs
            .AsNoTracking()
            .Where(r => r.ChannelId == channelId && externalProductIds.Contains(r.ExternalProductId))
            .ToDictionaryAsync(r => r.ExternalProductId, r => r.ChannelProductId, StringComparer.Ordinal, cancellationToken);
    }

    private async Task<OrderUpsertStatus> UpsertOrderAsync(
        Guid channelId,
        Guid connectionId,
        ChannelOrderSnapshot snapshot,
        Dictionary<string, Guid> channelProductIdsByExternalProductId,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        Order? existingOrder = await _context.Orders
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(
                o => o.ChannelId == channelId && o.ExternalOrderId == snapshot.ExternalOrderId,
                cancellationToken);

        bool isNew = existingOrder == null;

        Order order = existingOrder ?? new Order
        {
            Id = Guid.NewGuid(),
            ChannelId = channelId,
            ExternalOrderId = snapshot.ExternalOrderId,
            FirstImportedAt = now,
            CreatedAt = now,
        };

        if (isNew)
        {
            _context.Orders.Add(order);
        }

        // Captured before the snapshot is applied: the reduced placement instant is the minimum over
        // every report that ever described this cart, so the instant already persisted has to stay in
        // the candidate set or a delta run covering one unit would move the cart's date forward.
        DateTimeOffset previouslyImportedOrderDate = order.OrderDate;

        ApplyOrderSnapshot(order, snapshot, connectionId);

        // Lines are appended before change detection runs: EF only discovers a new child added to
        // a tracked collection's navigation during DetectChanges, not at the moment it is appended.
        foreach (ChannelOrderLineSnapshot lineSnapshot in snapshot.Lines)
        {
            UpsertOrderLine(order, lineSnapshot, channelProductIdsByExternalProductId, now);
        }

        // After the lines are upserted, because the status must cover every persisted line of the
        // order, including the ones an earlier run contributed and this snapshot does not mention.
        ApplyReducedOrderHeader(order, snapshot, isNew, previouslyImportedOrderDate);

        // Without this call every property assignment above is invisible to the change tracker,
        // so State stays Unchanged and both the counters and the resulting UPDATE would be wrong.
        _context.ChangeTracker.DetectChanges();

        bool orderChanged = isNew || HasMeaningfulOrderChanges(_context.Entry(order), BookkeepingProperties);

        bool anyLineChanged = HasMeaningfulLineChanges(order);

        if (isNew)
        {
            order.LastImportedAt = now;
            return OrderUpsertStatus.Created;
        }

        if (orderChanged || anyLineChanged)
        {
            order.LastImportedAt = now;
            order.UpdatedAt = now;
            return OrderUpsertStatus.Updated;
        }

        // Everything matched: no UPDATE is issued, so LastImportedAt deliberately stays put.
        return OrderUpsertStatus.Skipped;
    }

    private static bool HasMeaningfulOrderChanges(EntityEntry<Order> entry, HashSet<string> excludedProperties)
    {
        if (entry.State != EntityState.Modified)
        {
            return false;
        }

        foreach (PropertyEntry property in entry.Properties)
        {
            if (property.IsModified && !excludedProperties.Contains(property.Metadata.Name))
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
    private bool HasMeaningfulLineChanges(Order order)
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

    /// <summary>
    /// Copies the fields the snapshot reports directly. Everything the cart's own line set decides
    /// (status, cancel reason, placement instant, change instant, reported total) is applied
    /// afterwards by <see cref="ApplyReducedOrderHeader"/>: copying those from the snapshot would let
    /// a report that saw one unit of a multi-line cart overwrite the header of the whole cart.
    /// </summary>
    private static void ApplyOrderSnapshot(Order order, ChannelOrderSnapshot snapshot, Guid connectionId)
    {
        order.ConnectionId = connectionId;
        order.ExternalOrderNumber = snapshot.ExternalOrderNumber;
        order.ExternalSubstatus = snapshot.ExternalSubstatus;
        order.Currency = snapshot.Currency;
        order.DeliveryCity = snapshot.DeliveryCity;
        order.DeliveryRegion = snapshot.DeliveryRegion;
        order.WarehouseName = snapshot.WarehouseName;
        order.IsSellerWarehouse = snapshot.IsSellerWarehouse;
        order.IsB2b = snapshot.IsB2b;
        order.IsTestOrder = snapshot.IsTestOrder;
        order.ChannelReportedDeliveryFee = snapshot.ChannelReportedDeliveryFee;
    }

    /// <summary>
    /// Derives the cart-level fields from every line persisted for the order, which is the only line
    /// set that describes the whole cart. A delta run that observes a single unit therefore leaves
    /// the status of a multi-line order consistent with all of its lines instead of publishing the
    /// status of the one unit it happened to see.
    /// </summary>
    private static void ApplyReducedOrderHeader(
        Order order,
        ChannelOrderSnapshot snapshot,
        bool isNew,
        DateTimeOffset previouslyImportedOrderDate)
    {
        List<ChannelOrderLineSnapshot> persistedLines = new List<ChannelOrderLineSnapshot>(order.Lines.Count);

        foreach (OrderLine line in order.Lines)
        {
            // Only the facts the reduction reads are projected. The reported total is taken from the
            // incoming snapshot where this run carries that line, because it is not persisted on the
            // line; a persisted line the snapshot omits keeps a null total, which the reduction then
            // discards anyway unless the cart holds exactly one line.
            decimal? channelReportedTotal = null;

            foreach (ChannelOrderLineSnapshot snapshotLine in snapshot.Lines)
            {
                if (string.Equals(snapshotLine.LineKey, line.LineKey, StringComparison.Ordinal))
                {
                    channelReportedTotal = snapshotLine.ChannelReportedTotal;
                    break;
                }
            }

            persistedLines.Add(new ChannelOrderLineSnapshot
            {
                LineKey = line.LineKey,
                IsCancelled = line.IsCancelled,
                StatusChangedAt = line.StatusChangedAt,
                ChannelReportedTotal = channelReportedTotal,
                FulfillmentSchema = line.FulfillmentSchema
            });
        }

        // An order persisted without any line carries nothing to reduce; the snapshot is the whole
        // description of the cart in that case, and its own line set goes through the same reduction
        // as the mapper applied.
        if (persistedLines.Count == 0)
        {
            if (snapshot.Lines.Count == 0)
            {
                return;
            }

            persistedLines = snapshot.Lines;
        }

        List<DateTimeOffset> rowOrderDates = isNew
            ? new List<DateTimeOffset> { snapshot.OrderDate }
            : new List<DateTimeOffset> { previouslyImportedOrderDate, snapshot.OrderDate };

        ChannelOrderHeaderFacts header = ChannelOrderHeaderFacts.Reduce(
            persistedLines,
            rowOrderDates,
            snapshot.ReportsHeaderStatus ? snapshot.ExternalStatus : null,
            snapshot.ReportsHeaderStatus ? snapshot.CancelReason : null);

        order.ExternalStatus = header.ExternalStatus;
        order.Status = MapStatus(header.ExternalStatus);
        order.CancelReason = header.CancelReason;
        order.OrderDate = header.OrderDate;
        order.StatusChangedAt = header.StatusChangedAt;
        order.ChannelReportedTotal = header.ChannelReportedTotal;

        // Recomputed from the persisted line set rather than left as whatever ApplyOrderSnapshot
        // copied from this run's own snapshot: a mixed cart (FBO and FBS postings under one
        // order_number) is written by two separate runs, so a single run only ever sees its own
        // half of the fulfilment schema mix and would otherwise publish that half as the answer
        // for the whole cart.
        //
        // Only when the persisted set actually carries schemas. Wildberries reports the flag
        // itself and never sets one, so reducing its lines would replace a reported fact with a
        // value derived from data the channel does not publish.
        if (TryReduceFulfillmentSchema(persistedLines, out bool? isSellerWarehouse))
        {
            order.IsSellerWarehouse = isSellerWarehouse;
        }
    }

    /// <summary>
    /// Reduces the persisted lines' fulfilment schemas to the order-level flag: true when every line
    /// carrying a schema is seller-fulfilled (fbs/rfbs), false when they are all Ozon-fulfilled, null
    /// when the cart mixes both. Ozon only.
    /// </summary>
    /// <param name="lines">The whole persisted line set of the cart.</param>
    /// <param name="isSellerWarehouse">The reduced flag, meaningless when the method returns false.</param>
    /// <returns>
    /// False when no line carries a schema, which means the reduction has no input and the caller
    /// must keep whatever the channel reported. Lines without a schema are skipped rather than
    /// counted as Ozon-fulfilled: after the FBO half of a mixed cart has been imported and the FBS
    /// half has not, that would report a confident "Ozon warehouse" for a cart that is neither.
    /// </returns>
    private static bool TryReduceFulfillmentSchema(
        List<ChannelOrderLineSnapshot> lines, out bool? isSellerWarehouse)
    {
        bool hasOzonFulfilled = false;
        bool hasSellerFulfilled = false;
        bool hasSchema = false;

        foreach (ChannelOrderLineSnapshot line in lines)
        {
            string? schema = line.FulfillmentSchema;

            if (string.IsNullOrEmpty(schema))
            {
                continue;
            }

            hasSchema = true;

            if (string.Equals(schema, "fbs", StringComparison.OrdinalIgnoreCase)
                || string.Equals(schema, "rfbs", StringComparison.OrdinalIgnoreCase))
            {
                hasSellerFulfilled = true;
            }
            else
            {
                hasOzonFulfilled = true;
            }
        }

        if (!hasSchema)
        {
            isSellerWarehouse = null;
            return false;
        }

        isSellerWarehouse = hasSellerFulfilled && hasOzonFulfilled
            ? null
            : hasSellerFulfilled;

        return true;
    }

    /// <summary>
    /// Upserts a single order line. Line-level outcomes are not reported separately: the job-level
    /// counters track orders, per the "Processed N records" (orders, not lines) progress convention.
    /// </summary>
    private void UpsertOrderLine(
        Order order,
        ChannelOrderLineSnapshot snapshot,
        Dictionary<string, Guid> channelProductIdsByExternalProductId,
        DateTimeOffset now)
    {
        OrderLine? existingLine = order.Lines.FirstOrDefault(l => l.LineKey == snapshot.LineKey);

        OrderLine line;

        if (existingLine == null)
        {
            line = new OrderLine
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                LineKey = snapshot.LineKey,
                CreatedAt = now,
            };

            // Explicit DbSet.Add is required instead of only appending to order.Lines: when the
            // owning Order is already tracked (not newly Added), EF's local change detection
            // classifies a line appended only via the navigation collection as Modified rather
            // than Added, because a non-default PK gives it no Added semantics on its own.
            // Added-only entries are what SetTenantId()/ValidateCrossTenantWrite() rely on to tell
            // a fresh row from an existing one; without this the shadow TenantId is never
            // populated and the save fails with a cross-tenant guard exception.
            //
            // order.Lines.Add(line) must NOT also be called here: EF's relationship fixup already
            // adds the tracked line to the owning Order's Lines collection as soon as line.OrderId
            // resolves to a tracked Order (true for both a brand-new Order and one loaded via
            // Include) - adding it a second time via the navigation duplicates the same reference
            // in the in-memory list, which both inflates Lines.Count and corrupts anyLineChanged.
            _context.OrderLines.Add(line);
        }
        else
        {
            line = existingLine;
        }

        Guid? channelProductId = channelProductIdsByExternalProductId.TryGetValue(snapshot.ExternalProductId, out Guid resolvedId)
            ? resolvedId
            : null;

        line.ExternalLineId = snapshot.ExternalLineId;
        line.ExternalProductId = snapshot.ExternalProductId;
        line.OfferId = snapshot.OfferId;
        line.ExternalSizeId = snapshot.ExternalSizeId;
        line.ProductName = snapshot.ProductName;
        line.Vat = snapshot.Vat;
        line.Quantity = snapshot.Quantity;
        line.UnitPrice = snapshot.UnitPrice;
        line.BuyerUnitPrice = snapshot.BuyerUnitPrice;
        line.UnitPriceBeforeDiscount = snapshot.UnitPriceBeforeDiscount;
        line.Currency = snapshot.Currency;
        line.IsCancelled = snapshot.IsCancelled;
        line.StatusChangedAt = snapshot.StatusChangedAt;
        line.FulfillmentSchema = snapshot.FulfillmentSchema;
        line.ChannelProductId = channelProductId;
    }

    /// <summary>
    /// Reduces the verbatim channel status to the canonical, channel-independent projection.
    /// Serves both wired channels: Wildberries (which reports isCancel/active only, see
    /// WildberriesApiClient.MapToOrderSnapshot) and Ozon (which reports its own posting statuses,
    /// enabled by ChannelOrderSnapshot.ReportsHeaderStatus). The verbatim status is preserved in
    /// Order.ExternalStatus and is only projected here, so this projection stays re-derivable from
    /// the entity; an unrecognised status degrades to Unknown rather than failing the batch.
    /// </summary>
    /// <param name="externalStatus">Verbatim channel status, matched case-insensitively.</param>
    /// <returns>The projected order status.</returns>
    internal static OrderStatus MapStatus(string externalStatus)
    {
        if (string.IsNullOrWhiteSpace(externalStatus))
        {
            return OrderStatus.Unknown;
        }

        return externalStatus.Trim().ToLowerInvariant() switch
        {
            // Wildberries synonyms and the channel-neutral spellings. The Ozon statuses that are
            // literally identical to these ("delivered", "cancelled") are folded into these arms
            // rather than repeated below, since a switch rejects duplicate constant arms.
            "active" => OrderStatus.Processing,
            "created" or "new" => OrderStatus.Created,
            "processing" or "confirm" or "assembling" => OrderStatus.Processing,
            "shipped" or "sorted" or "sent" => OrderStatus.Shipped,
            "delivered" or "sold" or "buyout" => OrderStatus.Delivered,
            "cancelled" or "canceled" or "cancel" => OrderStatus.Cancelled,
            "returned" or "return" => OrderStatus.Returned,
            "partiallyreturned" or "partially_returned" => OrderStatus.PartiallyReturned,
            "unresolved" or "dispute" => OrderStatus.Unresolved,

            // Ozon-only statuses: rejected by Wildberries' feed, so they cannot collide above.
            // "arbitration"/"not_accepted" mean operator attention, not that the posting shipped.
            "awaiting_registration" => OrderStatus.Created,
            "awaiting_approve" => OrderStatus.Processing,
            "awaiting_packaging" => OrderStatus.Processing,
            "acceptance_in_progress" => OrderStatus.Processing,
            "awaiting_deliver" => OrderStatus.Processing,
            "driver_pickup" => OrderStatus.Shipped,
            "sent_by_seller" => OrderStatus.Shipped,
            "delivering" => OrderStatus.Shipped,
            "arbitration" => OrderStatus.Unresolved,
            "client_arbitration" => OrderStatus.Unresolved,
            "not_accepted" => OrderStatus.Unresolved,
            _ => OrderStatus.Unknown,
        };
    }

    private enum OrderUpsertStatus
    {
        Created,
        Updated,
        Skipped,
    }
}
