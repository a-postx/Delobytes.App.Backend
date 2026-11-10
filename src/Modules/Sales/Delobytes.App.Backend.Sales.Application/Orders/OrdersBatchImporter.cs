using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using Delobytes.App.Backend.Integrations.Contracts.Models;
using Delobytes.App.Backend.Sales.Application.Interfaces;
using Delobytes.App.Backend.Sales.Domain.Entities;
using Delobytes.App.Backend.Sales.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Sales.Application.Orders;

/// <summary>
/// Imports one batch of channel orders into the Sales module: upserts each order with its lines,
/// reduces the cart-level header from the persisted line set, and reports per-batch counters.
/// Persistence mechanics — change tracking and save retries — live behind
/// <see cref="IOrdersImportStore"/>.
/// </summary>
public class OrdersBatchImporter
{
    private readonly IOrdersImportStore _store;
    private readonly IEventPublisher _eventPublisher;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<OrdersBatchImporter> _logger;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="OrdersBatchImporter"/> class.
    /// </summary>
    /// <param name="store">Persistence port of the import.</param>
    /// <param name="eventPublisher">Event publisher.</param>
    /// <param name="tenantContext">Tenant context, used for diagnostics.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="timeProvider">Clock source.</param>
    public OrdersBatchImporter(
        IOrdersImportStore store,
        IEventPublisher eventPublisher,
        ITenantContext tenantContext,
        ILogger<OrdersBatchImporter> logger,
        TimeProvider timeProvider)
    {
        _store = store;
        _eventPublisher = eventPublisher;
        _tenantContext = tenantContext;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Processes the OrdersBatchImportRequestedEvent message.
    /// </summary>
    /// <param name="message">Event message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ProcessAsync(OrdersBatchImportRequestedEvent message, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "OrdersBatchImporter started processing batch. JobRunId={JobRunId}, BatchId={BatchId}, ConnectionId={ConnectionId}, ChannelId={ChannelId}, TenantId={TenantId}, OrdersCount={OrdersCount}",
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
        Dictionary<string, Guid> channelProductIdsByExternalProductId = await _store.LoadChannelProductIdsAsync(
            message.ChannelId, CollectExternalProductIds(message.Orders), cancellationToken);

        foreach (ChannelOrderSnapshot orderSnapshot in message.Orders)
        {
            try
            {
                OrderUpsertStatus status = await _store.ExecuteWithSaveAsync(
                    () => UpsertOrderAsync(
                        message.ChannelId,
                        message.ConnectionId,
                        orderSnapshot,
                        channelProductIdsByExternalProductId,
                        cancellationToken),
                    orderSnapshot.ExternalOrderId,
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

        await _eventPublisher.PublishAsync(completedEvent, cancellationToken);

        _logger.LogInformation(
            "OrdersBatchImporter completed batch. JobRunId={JobRunId}, BatchId={BatchId}, Created={Created}, Updated={Updated}, Skipped={Skipped}, Failed={Failed}",
            message.JobRunId,
            message.BatchId,
            created,
            updated,
            skipped,
            failed);
    }

    private static IReadOnlyCollection<string> CollectExternalProductIds(List<ChannelOrderSnapshot> orders)
    {
        return orders
            .SelectMany(o => o.Lines)
            .Select(l => l.ExternalProductId)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private async Task<OrderUpsertStatus> UpsertOrderAsync(
        Guid channelId,
        Guid connectionId,
        ChannelOrderSnapshot snapshot,
        Dictionary<string, Guid> channelProductIdsByExternalProductId,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();

        Order? existingOrder = await _store.FindOrderAsync(channelId, snapshot.ExternalOrderId, cancellationToken);

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
            _store.AddOrder(order);
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
        _store.DetectChanges();

        bool orderChanged = isNew || _store.HasMeaningfulOrderChanges(order);

        bool anyLineChanged = _store.HasMeaningfulLineChanges(order);

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
                ChannelReportedTotal = channelReportedTotal
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

            // Explicit Add is required instead of only appending to order.Lines: when the
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
            _store.AddOrderLine(line);
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
    public static OrderStatus MapStatus(string externalStatus)
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
