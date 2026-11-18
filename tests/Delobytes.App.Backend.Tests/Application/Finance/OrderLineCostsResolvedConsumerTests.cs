using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Catalog.Contracts.Events;
using Delobytes.App.Backend.Finance.Application.Projection;
using Delobytes.App.Backend.Finance.Domain.Entities;
using Delobytes.App.Backend.Finance.Domain.Enums;
using Delobytes.App.Backend.Finance.Infrastructure.Messaging.Consumers;
using ContractCostStatus = Delobytes.App.Backend.Catalog.Contracts.Events.OrderLineCostStatus;
using DomainCostStatus = Delobytes.App.Backend.Finance.Domain.Enums.OrderLineCostStatus;
using Delobytes.App.Backend.Tests.Application.Finance.Fakes;
using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Finance;

/// <summary>
/// Tests for <see cref="OrderLineCostsResolvedConsumer"/>. Catalog may resolve the same line more
/// than once and messages are redelivered, so the latest resolve must win and an equal timestamp
/// must be a no-op.
/// </summary>
public class OrderLineCostsResolvedConsumerTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Consume_StaleResult_IsIgnored()
    {
        // Arrange
        Guid orderLineId = Guid.NewGuid();

        InMemoryOrderLineCostStore costs = new InMemoryOrderLineCostStore();
        costs.Costs[orderLineId] = StoredCost(orderLineId, DomainCostStatus.Resolved, 100m, Now.AddMinutes(5));

        InMemoryPnlLineStore lines = new InMemoryPnlLineStore();
        OrderLineCostsResolvedConsumer consumer = CreateConsumer(costs, lines);

        // Act
        await consumer.Consume(Context(Result(orderLineId, ContractCostStatus.Resolved, 50m, Now)));

        // Assert
        costs.Costs[orderLineId].TotalCost.Should().Be(100m);
        lines.Lines.Should().BeEmpty("a stale result must not trigger a recompute");
    }

    [Fact]
    public async Task Consume_EqualResolvedAt_IsANoOp()
    {
        // Arrange
        Guid orderLineId = Guid.NewGuid();

        InMemoryOrderLineCostStore costs = new InMemoryOrderLineCostStore();
        costs.Costs[orderLineId] = StoredCost(orderLineId, DomainCostStatus.Resolved, 100m, Now);

        InMemoryPnlLineStore lines = new InMemoryPnlLineStore();
        OrderLineCostsResolvedConsumer consumer = CreateConsumer(costs, lines);

        // Act
        await consumer.Consume(Context(Result(orderLineId, ContractCostStatus.Resolved, 50m, Now)));

        // Assert
        costs.Costs[orderLineId].TotalCost.Should().Be(100m);
        lines.Lines.Should().BeEmpty();
    }

    [Fact]
    public async Task Consume_NotLinkedReplacedByResolved_IsAcceptedAndRecomputes()
    {
        // Arrange: the line was not linked when the previous result arrived.
        Guid orderLineId = Guid.NewGuid();

        InMemoryOrderLineCostStore costs = new InMemoryOrderLineCostStore();
        costs.Costs[orderLineId] = StoredCost(orderLineId, DomainCostStatus.ProductNotLinked, null, Now.AddMinutes(-1));

        InMemoryPnlLineStore lines = new InMemoryPnlLineStore();
        OrderLineCostsResolvedConsumer consumer = CreateConsumer(costs, lines, orderLineId);

        // Act
        await consumer.Consume(Context(Result(orderLineId, ContractCostStatus.Resolved, 50m, Now)));

        // Assert
        costs.Costs[orderLineId].Status.Should().Be(DomainCostStatus.Resolved);
        costs.Costs[orderLineId].TotalCost.Should().Be(50m);
        lines.Lines.Should().ContainKey(orderLineId);
    }

    private static OrderLineCostsResolvedConsumer CreateConsumer(
        InMemoryOrderLineCostStore costs,
        InMemoryPnlLineStore lines,
        Guid? orderLineId = null)
    {
        FixedTimeProvider timeProvider = new FixedTimeProvider(Now);
        InMemoryOrderLineRefStore refs = new InMemoryOrderLineRefStore();

        if (orderLineId.HasValue)
        {
            refs.Refs[orderLineId.Value] = new OrderLineRef
            {
                OrderLineId = orderLineId.Value,
                OrderId = Guid.NewGuid(),
                ChannelId = Guid.NewGuid(),
                ExternalOrderId = "order-1",
                ExternalLineId = "srid-1",
                ExternalProductId = "sku-1",
                Quantity = 1,
                OrderDate = Now,
                Currency = "RUB",
                UpdatedAt = Now,
            };
        }

        PnlLineProjector projector = new PnlLineProjector(
            new InMemoryPnlJournalStore(),
            refs,
            costs,
            lines,
            timeProvider);

        return new OrderLineCostsResolvedConsumer(
            costs,
            projector,
            timeProvider,
            NullLogger<OrderLineCostsResolvedConsumer>.Instance);
    }

    private static ConsumeContext<OrderLineCostsResolvedEvent> Context(OrderLineCostResult result)
    {
        Mock<ConsumeContext<OrderLineCostsResolvedEvent>> context = new Mock<ConsumeContext<OrderLineCostsResolvedEvent>>();

        context.SetupGet(c => c.Message).Returns(new OrderLineCostsResolvedEvent
        {
            Results = new List<OrderLineCostResult> { result },
        });

        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);

        return context.Object;
    }

    private static OrderLineCostResult Result(
        Guid orderLineId,
        ContractCostStatus status,
        decimal? totalCost,
        DateTimeOffset resolvedAt)
    {
        return new OrderLineCostResult
        {
            OrderLineId = orderLineId,
            Status = status,
            ProductId = status == ContractCostStatus.Resolved ? Guid.NewGuid() : null,
            AsOfDate = new DateOnly(2026, 8, 1),
            Quantity = 1,
            UnitMaterialCost = totalCost,
            UnitTotalCost = totalCost,
            TotalCost = totalCost,
            IsComplete = true,
            ResolvedAt = resolvedAt,
        };
    }

    private static OrderLineCost StoredCost(
        Guid orderLineId,
        DomainCostStatus status,
        decimal? totalCost,
        DateTimeOffset resolvedAt)
    {
        return new OrderLineCost
        {
            OrderLineId = orderLineId,
            Status = status,
            ProductId = status == DomainCostStatus.Resolved ? Guid.NewGuid() : null,

            AsOfDate = new DateOnly(2026, 8, 1),
            Quantity = 1,
            UnitTotalCost = totalCost,
            TotalCost = totalCost,
            IsComplete = true,
            ResolvedAt = resolvedAt,
            CreatedAt = resolvedAt,
        };
    }
}




