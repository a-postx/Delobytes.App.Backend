using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Finance.Application.Projection;
using Delobytes.App.Backend.Finance.Infrastructure.Messaging.Consumers;
using Delobytes.App.Backend.Sales.Contracts.Events;
using Delobytes.App.Backend.Tests.Application.Finance.Fakes;
using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Finance;

/// <summary>
/// Tests for <see cref="OrderLinesChangedConsumer"/>. The event is redelivered on any failure, so
/// the projection upsert and the recompute must both be idempotent.
/// </summary>
public class OrderLinesChangedConsumerTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Consume_UpsertsRefsAndRecomputesTheLines()
    {
        // Arrange
        InMemoryPnlJournalStore journal = new InMemoryPnlJournalStore();
        InMemoryOrderLineRefStore refs = new InMemoryOrderLineRefStore();
        InMemoryPnlLineStore lines = new InMemoryPnlLineStore();

        PnlLineProjector projector = new PnlLineProjector(
            journal,
            refs,
            new InMemoryOrderLineCostStore(),
            new InMemoryPnlExpectationStore(),
            lines,
            new FixedTimeProvider(Now),
            NullLogger<PnlLineProjector>.Instance);

        OrderLinesChangedConsumer consumer = new OrderLinesChangedConsumer(
            refs,
            projector,
            new FixedTimeProvider(Now),
            NullLogger<OrderLinesChangedConsumer>.Instance);

        ChangedOrderLine line = new ChangedOrderLine
        {
            OrderLineId = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            ChannelId = Guid.NewGuid(),
            ExternalOrderId = "order-1",
            ExternalLineId = "srid-1",
            ExternalProductId = "sku-1",
            Quantity = 1,
            OrderDate = Now,
            Currency = "RUB",
            IsNew = true,
            CostRelevantChange = true,
        };

        Mock<ConsumeContext<OrderLinesChangedEvent>> context = new Mock<ConsumeContext<OrderLinesChangedEvent>>();
        context.SetupGet(c => c.Message).Returns(new OrderLinesChangedEvent { Lines = new List<ChangedOrderLine> { line } });
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);

        // Act
        await consumer.Consume(context.Object);
        await consumer.Consume(context.Object);

        // Assert
        refs.Refs.Should().ContainKey(line.OrderLineId);
        refs.Refs[line.OrderLineId].ExternalLineId.Should().Be("srid-1");
        refs.Refs.Should().HaveCount(1);

        lines.Lines.Should().ContainKey(line.OrderLineId);
    }
}
