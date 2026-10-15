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

namespace Delobytes.App.Backend.Sales.Infrastructure.Messaging.Consumers;

/// <summary>
/// Consumer for persisting imported order batches into the Sales module. Upserts Order by
/// (TenantId, ChannelId, ExternalOrderId) and OrderLine by (TenantId, OrderId, LineKey),
/// resolves ChannelProductId from the local ChannelProductRef projection, and reports the
/// outcome back to Integrations via OrdersBatchImportCompletedEvent.
/// </summary>
public class OrdersBatchImportRequestedConsumer
{
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
            "OrdersBatchImportRequestedConsumer started processing batch. JobRunId={JobRunId}, BatchId={BatchId}, ConnectionId={ConnectionId}, ChannelId={ChannelId}, TenantId={TenantId}, OrdersCount={OrdersCount}, IsTerminal={IsTerminal}",
            message.JobRunId,
            message.BatchId,
            message.ConnectionId,
            message.ChannelId,
            _tenantContext.TenantId,
            message.Orders.Count,
            message.IsTerminal);

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
            HashSet<object> trackedBefore = _context.ChangeTracker.Entries().Select(e => e.Entity).ToHashSet();

            try
            {
                OrderUpsertStatus status = await UpsertOrderAsync(
                    message.ChannelId,
                    message.ConnectionId,
                    orderSnapshot,
                    channelProductIdsByExternalProductId,
                    cancellationToken);

                await _context.SaveChangesAsync(cancellationToken);

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
                // No explicit transaction spans the batch, so a failed SaveChanges rolls back
                // only this order's implicit transaction. The change tracker, however, keeps
                // whatever entries were added for it, so they must be detached before the next
                // order is processed, otherwise that order's SaveChanges would try to flush them too.
                foreach (EntityEntry entry in _context.ChangeTracker.Entries().ToList())
                {
                    if (!trackedBefore.Contains(entry.Entity))
                    {
                        entry.State = EntityState.Detached;
                    }
                }

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

        EntityEntry<Order> orderEntry = _context.Entry(order);

        ApplyOrderSnapshot(order, snapshot, connectionId);

        // Properties are compared by value, so re-assigning identical data leaves the entry
        // Unchanged: IsModified below reflects only a genuine difference, not every upsert attempt.
        bool orderChanged = isNew || (orderEntry.State == EntityState.Modified
            && orderEntry.Properties.Any(p => p.IsModified));

        bool anyLineChanged = false;

        foreach (ChannelOrderLineSnapshot lineSnapshot in snapshot.Lines)
        {
            bool lineChanged = UpsertOrderLine(order, lineSnapshot, channelProductIdsByExternalProductId, now);
            anyLineChanged = anyLineChanged || lineChanged;
        }

        order.LastImportedAt = now;

        if (!isNew && (orderChanged || anyLineChanged))
        {
            order.UpdatedAt = now;
        }

        if (isNew)
        {
            return OrderUpsertStatus.Created;
        }

        return orderChanged || anyLineChanged ? OrderUpsertStatus.Updated : OrderUpsertStatus.Skipped;
    }

    private static void ApplyOrderSnapshot(Order order, ChannelOrderSnapshot snapshot, Guid connectionId)
    {
        order.ConnectionId = connectionId;
        order.ExternalOrderNumber = snapshot.ExternalOrderNumber;
        order.OrderDate = snapshot.OrderDate;
        order.StatusChangedAt = snapshot.StatusChangedAt;
        order.Status = MapStatus(snapshot.ExternalStatus);
        order.ExternalStatus = snapshot.ExternalStatus;
        order.ExternalSubstatus = snapshot.ExternalSubstatus;
        order.CancelReason = snapshot.CancelReason;
        order.Currency = snapshot.Currency;
        order.DeliveryCity = snapshot.DeliveryCity;
        order.DeliveryRegion = snapshot.DeliveryRegion;
        order.WarehouseName = snapshot.WarehouseName;
        order.IsSellerWarehouse = snapshot.IsSellerWarehouse;
        order.IsB2b = snapshot.IsB2b;
        order.IsTestOrder = snapshot.IsTestOrder;
        order.ChannelReportedTotal = snapshot.ChannelReportedTotal;
        order.ChannelReportedDeliveryFee = snapshot.ChannelReportedDeliveryFee;
    }

    /// <summary>
    /// Upserts a single order line. Returns true if the line was created or any field changed.
    /// Line-level outcomes are not reported separately: the job-level counters track orders,
    /// per the "Processed N records" (orders, not lines) progress convention.
    /// </summary>
    private bool UpsertOrderLine(
        Order order,
        ChannelOrderLineSnapshot snapshot,
        Dictionary<string, Guid> channelProductIdsByExternalProductId,
        DateTimeOffset now)
    {
        OrderLine? existingLine = order.Lines.FirstOrDefault(l => l.LineKey == snapshot.LineKey);
        bool isNew = existingLine == null;

        OrderLine line = existingLine ?? new OrderLine
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            LineKey = snapshot.LineKey,
            CreatedAt = now,
        };

        if (isNew)
        {
            order.Lines.Add(line);
        }

        EntityEntry<OrderLine> lineEntry = _context.Entry(line);

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
        line.ChannelProductId = channelProductId;

        bool changed = isNew || (lineEntry.State == EntityState.Modified
            && lineEntry.Properties.Any(p => p.IsModified));

        if (!isNew && changed)
        {
            line.UpdatedAt = now;
        }

        return changed;
    }

    /// <summary>
    /// Reduces the verbatim channel status to the canonical, channel-independent projection.
    /// Only Wildberries' orders feed is wired up so far, which reports isCancel/active only
    /// (see WildberriesApiClient.MapToOrderSnapshot) &#8212; recognised synonyms from other
    /// channels are included so the mapping does not need to change the moment a second
    /// channel starts publishing orders.
    /// </summary>
    private static OrderStatus MapStatus(string externalStatus)
    {
        if (string.IsNullOrWhiteSpace(externalStatus))
        {
            return OrderStatus.Unknown;
        }

        return externalStatus.Trim().ToLowerInvariant() switch
        {
            "active" => OrderStatus.Processing,
            "created" or "new" => OrderStatus.Created,
            "processing" or "confirm" or "assembling" => OrderStatus.Processing,
            "shipped" or "sorted" or "sent" => OrderStatus.Shipped,
            "delivered" or "sold" or "buyout" => OrderStatus.Delivered,
            "cancelled" or "canceled" or "cancel" => OrderStatus.Cancelled,
            "returned" or "return" => OrderStatus.Returned,
            "partiallyreturned" or "partially_returned" => OrderStatus.PartiallyReturned,
            "unresolved" or "dispute" => OrderStatus.Unresolved,
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
