using Delobytes.App.Backend.Catalog.Contracts.Events;
using Delobytes.App.Backend.Sales.Application.Interfaces;
using Delobytes.App.Backend.Sales.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Sales.Application.Models;
using Delobytes.App.Backend.Sales.Application.Options;
using Delobytes.App.Backend.Sales.Contracts.Events;
using Delobytes.App.Backend.Sales.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Delobytes.App.Backend.Sales.Application.Services;

/// <summary>
/// Handles ChannelProductLinkedEvent, published by Catalog after a channel product link has been
/// created or updated. Upserts the Sales-side <see cref="ChannelProductRef"/> projection, which is
/// what <see cref="OrderLine.ChannelProductId"/> is resolved from during an order import, and
/// backfills lines whose pointer is missing or stale.
/// </summary>
public class ChannelProductLinker
{
    private readonly IChannelProductRefRepository _repository;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<ChannelProductLinker> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly IOptions<SalesImportOptions> _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChannelProductLinker"/> class.
    /// </summary>
    /// <param name="repository">Projection repository.</param>
    /// <param name="eventPublisher">Event publisher.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="timeProvider">Clock source.</param>
    /// <param name="options">Import tuning.</param>
    public ChannelProductLinker(
        IChannelProductRefRepository repository,
        IEventPublisher eventPublisher,
        ILogger<ChannelProductLinker> logger,
        TimeProvider timeProvider,
        IOptions<SalesImportOptions> options)
    {
        _repository = repository;
        _eventPublisher = eventPublisher;
        _logger = logger;
        _timeProvider = timeProvider;
        _options = options;
    }

    /// <summary>
    /// Upserts the projection and backfills the order lines it resolves.
    /// </summary>
    /// <param name="message">Event message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the projection and the lines are written.</returns>
    public async Task UpdateChannelProductRefAsync(ChannelProductLinkedEvent message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        DateTimeOffset now = _timeProvider.GetUtcNow();

        ChannelProductRef? projection = await _repository
            .FindByChannelProductIdAsync(message.ChannelProductId, cancellationToken);

        if (projection == null)
        {
            await RemoveStaleProjectionAsync(message, cancellationToken);

            projection = new ChannelProductRef
            {
                ChannelProductId = message.ChannelProductId,
            };

            _repository.Add(projection);

            _logger.LogInformation(
                "Created ChannelProductRef projection. ChannelProductId={ChannelProductId}, ChannelId={ChannelId}, ExternalProductId={ExternalProductId}",
                message.ChannelProductId,
                message.ChannelId,
                message.ExternalProductId);
        }

        projection.ProductId = message.ProductId;
        projection.ChannelId = message.ChannelId;
        projection.ExternalProductId = message.ExternalProductId;
        projection.ExternalSku = message.ExternalSku;
        projection.Sku = message.Sku;
        projection.ProductName = message.ProductName;
        projection.PhotoUrl = message.PhotoUrl;
        projection.UpdatedAt = now;

        await _repository.SaveChangesAsync(cancellationToken);

        await BackfillOrderLinesAsync(message, now, cancellationToken);
    }

    /// <summary>
    /// Points every line of the same channel and external product at the current channel product.
    /// A line linked to a deleted stale projection is repaired here too: after a relink the old
    /// <c>ChannelProductId</c> has no projection row left, so Catalog could never resolve its cost.
    /// </summary>
    private async Task BackfillOrderLinesAsync(
        ChannelProductLinkedEvent message,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<OrderLine> lines = await _repository.LoadOrderLinesToRelinkAsync(
            message.ChannelId,
            message.ExternalProductId,
            message.ChannelProductId,
            cancellationToken);

        if (lines.Count == 0)
        {
            return;
        }

        List<ChangedOrderLine> changedLines = new List<ChangedOrderLine>(lines.Count);

        foreach (OrderLine line in lines)
        {
            line.ChannelProductId = message.ChannelProductId;

            changedLines.Add(new ChangedOrderLine
            {
                OrderLineId = line.Id,
                OrderId = line.OrderId,
                ChannelId = line.Order.ChannelId,
                ExternalOrderId = line.Order.ExternalOrderId,
                ExternalLineId = line.ExternalLineId,
                ExternalProductId = line.ExternalProductId,
                ChannelProductId = line.ChannelProductId,
                Quantity = line.Quantity,
                OrderDate = line.Order.OrderDate,
                Currency = line.Currency,
                IsCancelled = line.IsCancelled,
                IsNew = false,
                CostRelevantChange = true,
            });
        }

        await _repository.SaveChangesAsync(cancellationToken);

        int chunkSize = _options.Value.OrderLinesChangedChunkSize > 0
            ? _options.Value.OrderLinesChangedChunkSize
            : 500;

        for (int offset = 0; offset < changedLines.Count; offset += chunkSize)
        {
            List<ChangedOrderLine> chunk = changedLines.GetRange(
                offset,
                Math.Min(chunkSize, changedLines.Count - offset));

            await _eventPublisher.PublishAsync(
                new OrderLinesChangedEvent { Lines = chunk },
                cancellationToken);
        }

        _logger.LogInformation(
            "Backfilled order line links. ChannelId={ChannelId}, ExternalProductId={ExternalProductId}, ChannelProductId={ChannelProductId}, LinesCount={LinesCount}, At={At}",
            message.ChannelId,
            message.ExternalProductId,
            message.ChannelProductId,
            changedLines.Count,
            now);
    }

    /// <summary>
    /// Drops a projection row that still occupies the natural key
    /// <c>(TenantId, ChannelId, ExternalProductId)</c> under a different
    /// <c>ChannelProductId</c>, which happens when a product is relinked and the catalogue issues a
    /// new link identity for the same marketplace item.
    /// </summary>
    /// <remarks>
    /// Removed and saved on its own instead of in the same batch as the insert: EF Core gives no
    /// ordering guarantee between a delete and an insert of rows sharing a unique key, and the
    /// insert hitting the index first would fail the batch. Only the collision path pays for the
    /// extra round trip.
    /// </remarks>
    /// <param name="message">Event message carrying the incoming natural key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task RemoveStaleProjectionAsync(
        ChannelProductLinkedEvent message, CancellationToken cancellationToken)
    {
        ChannelProductRef? staleProjection = await _repository.FindStaleAsync(
            message.ChannelId,
            message.ExternalProductId,
            message.ChannelProductId,
            cancellationToken);

        if (staleProjection == null)
        {
            return;
        }

        _repository.Remove(staleProjection);

        await _repository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Replaced stale ChannelProductRef projection. StaleChannelProductId={StaleChannelProductId}, NewChannelProductId={NewChannelProductId}, ChannelId={ChannelId}, ExternalProductId={ExternalProductId}",
            staleProjection.ChannelProductId,
            message.ChannelProductId,
            message.ChannelId,
            message.ExternalProductId);
    }
}
