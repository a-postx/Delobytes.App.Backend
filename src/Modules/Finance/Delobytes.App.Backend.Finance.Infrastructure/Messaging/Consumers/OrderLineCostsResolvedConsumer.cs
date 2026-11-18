using Delobytes.App.Backend.Catalog.Contracts.Events;
using Delobytes.App.Backend.Finance.Application.Interfaces;
using Delobytes.App.Backend.Finance.Application.Projection;
using Delobytes.App.Backend.Finance.Domain.Entities;
using Delobytes.App.Backend.Finance.Domain.Enums;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Finance.Infrastructure.Messaging.Consumers;

/// <summary>
/// Stores the production costs Catalog resolved and recomputes the affected P&amp;L lines. The
/// store keeps the result with the latest <c>ResolvedAt</c>, so a redelivered or stale message
/// changes nothing.
/// </summary>
public class OrderLineCostsResolvedConsumer : IConsumer<OrderLineCostsResolvedEvent>
{
    private readonly IOrderLineCostStore _costs;
    private readonly PnlLineProjector _projector;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OrderLineCostsResolvedConsumer> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="OrderLineCostsResolvedConsumer"/> class.
    /// </summary>
    /// <param name="costs">Cost store.</param>
    /// <param name="projector">P&amp;L line projector.</param>
    /// <param name="timeProvider">Clock source.</param>
    /// <param name="logger">Logger instance.</param>
    public OrderLineCostsResolvedConsumer(
        IOrderLineCostStore costs,
        PnlLineProjector projector,
        TimeProvider timeProvider,
        ILogger<OrderLineCostsResolvedConsumer> logger)
    {
        _costs = costs;
        _projector = projector;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task Consume(ConsumeContext<OrderLineCostsResolvedEvent> context)
    {
        IReadOnlyList<OrderLineCostResult> results = context.Message.Results;

        if (results.Count == 0)
        {
            return;
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();

        int accepted = 0;
        int ignored = 0;

        foreach (OrderLineCostResult result in results)
        {
            OrderLineCost cost = new OrderLineCost
            {
                OrderLineId = result.OrderLineId,
                Status = MapStatus(result.Status),
                ProductId = result.ProductId,
                AsOfDate = result.AsOfDate,
                Quantity = result.Quantity,
                UnitMaterialCost = result.UnitMaterialCost,
                UnitLogisticsCost = result.UnitLogisticsCost,
                UnitPackagingCost = result.UnitPackagingCost,
                UnitLaborCost = result.UnitLaborCost,
                UnitTotalCost = result.UnitTotalCost,
                TotalCost = result.TotalCost,
                IsComplete = result.IsComplete,
                WarningTypes = result.WarningTypes,
                ResolvedAt = result.ResolvedAt,
                CreatedAt = now,
                UpdatedAt = now,
            };

            bool written = await _costs.TryUpsertNewerAsync(cost, context.CancellationToken);

            if (!written)
            {
                ignored++;
                continue;
            }

            accepted++;

            await _projector.RecomputeAsync(new[] { result.OrderLineId }, context.CancellationToken);
        }

        _logger.LogInformation(
            "Stored order line costs. Accepted={Accepted}, Ignored={Ignored}",
            accepted,
            ignored);
    }

    private static Finance.Domain.Enums.OrderLineCostStatus MapStatus(Catalog.Contracts.Events.OrderLineCostStatus status)
    {
        return status switch
        {
            Catalog.Contracts.Events.OrderLineCostStatus.Resolved => Finance.Domain.Enums.OrderLineCostStatus.Resolved,
            Catalog.Contracts.Events.OrderLineCostStatus.ProductNotLinked => Finance.Domain.Enums.OrderLineCostStatus.ProductNotLinked,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unmapped order line cost status."),
        };
    }
}
