using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using Delobytes.App.Backend.Integrations.Contracts.Models;
using Delobytes.App.Backend.Sales.Application.Interfaces;
using Delobytes.App.Backend.Sales.Application.Models;
using Delobytes.App.Backend.Sales.Application.Options;
using Delobytes.App.Backend.Sales.Application.Orders;
using Delobytes.App.Backend.Sales.Contracts.Events;
using Delobytes.App.Backend.Sales.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Sales;

/// <summary>
/// Tests for the order line change publication added to <see cref="OrdersBatchImporter"/>. Finance
/// can only bind money to a sale if it learns about the line, so a line of a created or updated
/// order must be published, while a skipped or failed order must not produce an event.
/// </summary>
public class OrdersBatchImporterPublishingTests
{
    private static readonly Guid ChannelId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ProcessAsync_CreatedOrder_PublishesEveryLineInChunks()
    {
        // Arrange
        FakeOrdersImportStore store = new FakeOrdersImportStore();
        RecordingSalesEventPublisher publisher = new RecordingSalesEventPublisher();

        OrdersBatchImporter importer = CreateImporter(store, publisher, chunkSize: 2);

        ChannelOrderSnapshot snapshot = new ChannelOrderSnapshot
        {
            ExternalOrderId = "order-1",
            OrderDate = Now,
            ExternalStatus = "created",
            Currency = "RUB",
            Lines = new List<ChannelOrderLineSnapshot>
            {
                Line("line-1", "sku-1"),
                Line("line-2", "sku-2"),
                Line("line-3", "sku-3"),
            },
        };

        foreach (ChannelOrderLineSnapshot line in snapshot.Lines)
        {
            store.Changes[line.LineKey] = new OrderLineChange(Guid.Empty, true, true);
        }

        // Act
        await importer.ProcessAsync(Message(snapshot), CancellationToken.None);

        // Assert
        List<OrderLinesChangedEvent> events = publisher.Events.OfType<OrderLinesChangedEvent>().ToList();
        events.Should().HaveCount(2);
        events.SelectMany(e => e.Lines).Should().HaveCount(3);

        events.SelectMany(e => e.Lines).Should().OnlyContain(l => l.IsNew && l.CostRelevantChange);
        events.SelectMany(e => e.Lines).Should().OnlyContain(l => l.ChannelId == ChannelId);
    }

    [Fact]
    public async Task ProcessAsync_UnchangedOrder_PublishesNothing()
    {
        // Arrange: the stored order already carries the same line, so the import is a no-op.
        OrderLine existingLine = new OrderLine
        {
            Id = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            LineKey = "line-1",
            ExternalProductId = "sku-1",
            Quantity = 1,
            Currency = "RUB",
        };

        Order existingOrder = new Order
        {
            Id = existingLine.OrderId,
            ChannelId = ChannelId,
            ExternalOrderId = "order-1",
            OrderDate = Now,
            Currency = "RUB",
            ExternalStatus = "created",
            Lines = new List<OrderLine> { existingLine },
        };

        existingLine.Order = existingOrder;

        FakeOrdersImportStore store = new FakeOrdersImportStore { ExistingOrder = existingOrder };

        RecordingSalesEventPublisher publisher = new RecordingSalesEventPublisher();
        OrdersBatchImporter importer = CreateImporter(store, publisher, chunkSize: 500);

        ChannelOrderSnapshot snapshot = new ChannelOrderSnapshot
        {
            ExternalOrderId = "order-1",
            OrderDate = Now,
            ExternalStatus = "created",
            Currency = "RUB",
            Lines = new List<ChannelOrderLineSnapshot> { Line("line-1", "sku-1") },
        };

        // Act
        await importer.ProcessAsync(Message(snapshot), CancellationToken.None);

        // Assert
        publisher.Events.OfType<OrderLinesChangedEvent>().Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessAsync_FailedOrder_IsCountedAndNotPublished()
    {
        // Arrange
        FakeOrdersImportStore store = new FakeOrdersImportStore
        {
            Failure = new InvalidOperationException("boom"),
        };

        RecordingSalesEventPublisher publisher = new RecordingSalesEventPublisher();
        OrdersBatchImporter importer = CreateImporter(store, publisher, chunkSize: 500);

        ChannelOrderSnapshot snapshot = new ChannelOrderSnapshot
        {
            ExternalOrderId = "order-1",
            OrderDate = Now,
            ExternalStatus = "created",
            Currency = "RUB",
            Lines = new List<ChannelOrderLineSnapshot> { Line("line-1", "sku-1") },
        };

        // Act
        await importer.ProcessAsync(Message(snapshot), CancellationToken.None);

        // Assert
        publisher.Events.OfType<OrderLinesChangedEvent>().Should().BeEmpty();

        OrdersBatchImportCompletedEvent completed = publisher.Events
            .OfType<OrdersBatchImportCompletedEvent>()
            .Should().ContainSingle().Subject;

        completed.Failed.Should().Be(1);
    }

    private static OrdersBatchImporter CreateImporter(
        FakeOrdersImportStore store,
        RecordingSalesEventPublisher publisher,
        int chunkSize)
    {
        Mock<ITenantContext> tenantContext = new Mock<ITenantContext>();
        tenantContext.Setup(t => t.TenantId).Returns(Guid.NewGuid());

        return new OrdersBatchImporter(
            store,
            publisher,
            tenantContext.Object,
            NullLogger<OrdersBatchImporter>.Instance,
            TimeProvider.System,
            Microsoft.Extensions.Options.Options.Create(new SalesImportOptions { OrderLinesChangedChunkSize = chunkSize }));
    }

    private static OrdersBatchImportRequestedEvent Message(ChannelOrderSnapshot snapshot)
    {
        return new OrdersBatchImportRequestedEvent
        {
            JobRunId = Guid.NewGuid(),
            ConnectionId = Guid.NewGuid(),
            ChannelId = ChannelId,
            BatchId = Guid.NewGuid(),
            Orders = new List<ChannelOrderSnapshot> { snapshot },
        };
    }

    private static ChannelOrderLineSnapshot Line(string lineKey, string externalProductId)
    {
        return new ChannelOrderLineSnapshot
        {
            LineKey = lineKey,
            ExternalLineId = lineKey,
            ExternalProductId = externalProductId,
            Quantity = 1,
            Currency = "RUB",
        };
    }

    private sealed class RecordingSalesEventPublisher : Delobytes.App.Backend.Sales.Application.Interfaces.IEventPublisher
    {
        public List<object> Events { get; } = new List<object>();

        public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken)
            where TEvent : class
        {
            Events.Add(@event);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeOrdersImportStore : IOrdersImportStore
    {
        public Order? ExistingOrder { get; set; }

        public Exception? Failure { get; set; }

        public Dictionary<string, OrderLineChange> Changes { get; } = new Dictionary<string, OrderLineChange>(StringComparer.Ordinal);

        public Task<Dictionary<string, Guid>> LoadChannelProductIdsAsync(
            Guid channelId,
            IReadOnlyCollection<string> externalProductIds,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new Dictionary<string, Guid>());
        }

        public Task<Order?> FindOrderAsync(Guid channelId, string externalOrderId, CancellationToken cancellationToken)
        {
            return Task.FromResult(ExistingOrder);
        }

        public void AddOrder(Order order)
        {
            ExistingOrder = order;
        }

        public void AddOrderLine(OrderLine line)
        {
            ExistingOrder?.Lines.Add(line);
        }

        public void DetectChanges()
        {
        }

        public bool HasMeaningfulOrderChanges(Order order)
        {
            return false;
        }

        public bool HasMeaningfulLineChanges(Order order)
        {
            return false;
        }

        public IReadOnlyList<OrderLineChange> DescribeLineChanges(Order order)
        {
            return order.Lines
                .Select(line => Changes.TryGetValue(line.LineKey, out OrderLineChange? change)
                    ? change with { OrderLineId = line.Id }
                    : new OrderLineChange(line.Id, false, false))
                .ToList();
        }

        public Task<TResult> ExecuteWithSaveAsync<TResult>(
            Func<Task<TResult>> operation,
            string externalOrderId,
            CancellationToken cancellationToken)
        {
            if (Failure != null)
            {
                throw Failure;
            }

            return operation();
        }
    }
}

