using Delobytes.App.Backend.Finance.Application.Interfaces;
using Delobytes.App.Backend.Finance.Application.Projection;
using Delobytes.App.Backend.Finance.Domain.Entities;
using Delobytes.App.Backend.Sales.Contracts.Events;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Finance.Infrastructure.Messaging.Consumers;

/// <summary>
/// Maintains the Finance-side order line projection from Sales change events and recomputes the
/// affected P&amp;L lines. The upsert is keyed by the order line identifier, so a redelivered event
/// changes nothing and the recompute is idempotent by construction.
/// </summary>
public class OrderLinesChangedConsumer : IConsumer<OrderLinesChangedEvent>
{
    private readonly IOrderLineRefStore _orderLineRefs;
    private readonly PnlLineProjector _projector;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OrderLinesChangedConsumer> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="OrderLinesChangedConsumer"/> class.
    /// </summary>
    /// <param name="orderLineRefs">Order line projection store.</param>
    /// <param name="projector">P&amp;L line projector.</param>
    /// <param name="timeProvider">Clock source.</param>
    /// <param name="logger">Logger instance.</param>
    public OrderLinesChangedConsumer(
        IOrderLineRefStore orderLineRefs,
        PnlLineProjector projector,
        TimeProvider timeProvider,
        ILogger<OrderLinesChangedConsumer> logger)
    {
        _orderLineRefs = orderLineRefs;
        _projector = projector;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task Consume(ConsumeContext<OrderLinesChangedEvent> context)
    {
        IReadOnlyList<ChangedOrderLine> lines = context.Message.Lines;

        if (lines.Count == 0)
        {
            return;
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();

        List<OrderLineRef> refs = new(lines.Count);
        List<Guid> lineIds = new(lines.Count);

        foreach (ChangedOrderLine line in lines)
        {
            refs.Add(new OrderLineRef
            {
                OrderLineId = line.OrderLineId,
                OrderId = line.OrderId,
                ChannelId = line.ChannelId,
                ExternalOrderId = line.ExternalOrderId,
                ExternalLineId = line.ExternalLineId,
                ExternalProductId = line.ExternalProductId,
                ChannelProductId = line.ChannelProductId,
                Quantity = line.Quantity,
                OrderDate = line.OrderDate,
                Currency = line.Currency,
                IsCancelled = line.IsCancelled,
                UpdatedAt = now,
            });

            lineIds.Add(line.OrderLineId);
        }

        await _orderLineRefs.UpsertAsync(refs, context.CancellationToken);

        await _projector.RecomputeAsync(lineIds, context.CancellationToken);

        _logger.LogInformation(
            "Upserted order line refs and recomputed P&L lines. LinesCount={LinesCount}",
            lines.Count);
    }
}
