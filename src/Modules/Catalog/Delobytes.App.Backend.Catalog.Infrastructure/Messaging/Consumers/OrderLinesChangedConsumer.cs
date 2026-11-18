using Delobytes.App.Backend.Catalog.Application.Interfaces;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Application.Services.CostCalculation;
using Delobytes.App.Backend.Catalog.Contracts.Events;
using Delobytes.App.Backend.Contracts.Accounting;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Sales.Contracts.Events;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Messaging.Consumers;

/// <summary>
/// Resolves the production cost of the order lines Sales reported and publishes the results for
/// Finance. The cost is as of the tenant-local order date: Sales stores no shipment date, and the
/// stock has no lot tracking, so any other date would be invented rather than observed.
/// </summary>
public class OrderLinesChangedConsumer : IConsumer<OrderLinesChangedEvent>
{
    private readonly IChannelProductRepository _channelProducts;
    private readonly ICostCalculator _costCalculator;
    private readonly ITenantTimeService _tenantTimeService;
    private readonly IEventPublisher _eventPublisher;
    private readonly ITenantContext _tenantContext;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OrderLinesChangedConsumer> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="OrderLinesChangedConsumer"/> class.
    /// </summary>
    /// <param name="channelProducts">Channel product repository.</param>
    /// <param name="costCalculator">Cost calculator.</param>
    /// <param name="tenantTimeService">Tenant time service.</param>
    /// <param name="eventPublisher">Event publisher.</param>
    /// <param name="tenantContext">Tenant context.</param>
    /// <param name="timeProvider">Clock source.</param>
    /// <param name="logger">Logger instance.</param>
    public OrderLinesChangedConsumer(
        IChannelProductRepository channelProducts,
        ICostCalculator costCalculator,
        ITenantTimeService tenantTimeService,
        IEventPublisher eventPublisher,
        ITenantContext tenantContext,
        TimeProvider timeProvider,
        ILogger<OrderLinesChangedConsumer> logger)
    {
        _channelProducts = channelProducts;
        _costCalculator = costCalculator;
        _tenantTimeService = tenantTimeService;
        _eventPublisher = eventPublisher;
        _tenantContext = tenantContext;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task Consume(ConsumeContext<OrderLinesChangedEvent> context)
    {
        List<ChangedOrderLine> lines = context.Message.Lines
            .Where(line => line.CostRelevantChange)
            .ToList();

        if (lines.Count == 0)
        {
            return;
        }

        Guid tenantId = _tenantContext.TenantId
            ?? throw new InvalidOperationException("Cannot resolve a cost without a resolved tenant.");

        TimeZoneInfo timeZone = await _tenantTimeService.GetTimeZoneAsync(tenantId, context.CancellationToken);

        DateTimeOffset now = _timeProvider.GetUtcNow();

        List<OrderLineCostResult> results = new List<OrderLineCostResult>(lines.Count);
        Dictionary<(Guid ProductId, DateOnly AsOfDate), CostBreakdown> calculations = new();

        foreach (ChangedOrderLine line in lines)
        {
            DateOnly asOf = TenantTimeZones.DateAt(line.OrderDate, timeZone);

            Guid? productId = line.ChannelProductId;

            if (!productId.HasValue)
            {
                productId = await _channelProducts.FindChannelProductIdAsync(
                    line.ChannelId,
                    line.ExternalProductId,
                    context.CancellationToken);
            }

            if (!productId.HasValue)
            {
                results.Add(new OrderLineCostResult
                {
                    OrderLineId = line.OrderLineId,
                    Status = OrderLineCostStatus.ProductNotLinked,
                    ProductId = null,
                    AsOfDate = asOf,
                    Quantity = line.Quantity,
                    IsComplete = false,
                    WarningTypes = new List<string>(),
                    ResolvedAt = now,
                });

                continue;
            }

            (Guid ProductId, DateOnly AsOfDate) key = (productId.Value, asOf);

            if (!calculations.TryGetValue(key, out CostBreakdown? breakdown))
            {
                breakdown = await _costCalculator.CalculateAsync(productId.Value, asOf, context.CancellationToken);
                calculations.Add(key, breakdown);
            }

            results.Add(new OrderLineCostResult
            {
                OrderLineId = line.OrderLineId,
                Status = OrderLineCostStatus.Resolved,
                ProductId = productId.Value,
                AsOfDate = asOf,
                Quantity = line.Quantity,
                UnitMaterialCost = breakdown.MaterialCost,
                UnitLogisticsCost = breakdown.LogisticsCost,
                UnitPackagingCost = breakdown.PackagingCost,
                UnitLaborCost = breakdown.LaborCost,
                UnitTotalCost = breakdown.TotalCost,
                TotalCost = breakdown.TotalCost * line.Quantity,
                IsComplete = breakdown.IsComplete,
                WarningTypes = breakdown.Warnings
                    .Select(warning => warning.Type.ToString())
                    .Distinct(StringComparer.Ordinal)
                    .ToList(),
                ResolvedAt = now,
            });
        }

        await _eventPublisher.PublishAsync(
            new OrderLineCostsResolvedEvent { Results = results },
            context.CancellationToken);

        _logger.LogInformation(
            "Resolved order line costs. LinesCount={LinesCount}, ProductsCalculated={ProductsCalculated}",
            results.Count,
            calculations.Count);
    }
}
