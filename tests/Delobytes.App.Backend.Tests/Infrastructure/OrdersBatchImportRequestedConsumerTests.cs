using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using Delobytes.App.Backend.Integrations.Contracts.Models;
using Delobytes.App.Backend.Sales.Domain.Entities;
using Delobytes.App.Backend.Sales.Domain.Enums;
using Delobytes.App.Backend.Sales.Infrastructure.Messaging.Consumers;
using Delobytes.App.Backend.Sales.Infrastructure.Persistence;
using FluentAssertions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Infrastructure;

/// <summary>
/// Tests for OrdersBatchImportRequestedConsumer. Covers idempotent order/line upsert, the
/// (ChannelId, ExternalProductId) resolution against the ChannelProductRef projection, counter
/// reporting back to Integrations, tenant isolation and per-order failure containment.
/// </summary>
public class OrdersBatchImportRequestedConsumerTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _channelId = Guid.NewGuid();
    private readonly Guid _connectionId = Guid.NewGuid();
    private readonly Guid _jobRunId = Guid.NewGuid();

    [Fact]
    public async Task ProcessAsync_CreatesOrderAndLine_WhenNoMatchExists()
    {
        // Arrange
        string databaseName = Guid.NewGuid().ToString();
        SalesDbContext consumerContext = BuildSalesDbContext(_tenantId, databaseName);

        Mock<IPublishEndpoint> publishEndpointMock = new Mock<IPublishEndpoint>();
        OrdersBatchImportRequestedConsumer consumer = BuildConsumer(consumerContext, publishEndpointMock.Object, _tenantId);

        ChannelOrderSnapshot snapshot = BuildOrderSnapshot("ORDER-1", "nm-100", "SRID-1", price: 1500m);

        // Act
        await consumer.ProcessAsync(BuildMessage(new List<ChannelOrderSnapshot> { snapshot }), CancellationToken.None);

        // Assert
        Order? order = await consumerContext.Orders
            .IgnoreQueryFilters()
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.ExternalOrderId == "ORDER-1");

        order.Should().NotBeNull();
        order!.ChannelId.Should().Be(_channelId);
        order.ConnectionId.Should().Be(_connectionId);
        order.Currency.Should().Be("RUB");
        order.Status.Should().Be(OrderStatus.Processing);
        order.ExternalStatus.Should().Be("active");
        order.ChannelReportedTotal.Should().Be(1700m);
        order.FirstImportedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
        order.UpdatedAt.Should().BeNull();

        order.Lines.Should().HaveCount(1);
        OrderLine line = order.Lines.First();
        line.LineKey.Should().Be("SRID-1");
        line.ExternalProductId.Should().Be("nm-100");
        line.UnitPrice.Should().Be(1500m);
        line.Quantity.Should().Be(1);
        line.ChannelProductId.Should().BeNull();

        publishEndpointMock.Verify(
            p => p.Publish(
                It.Is<OrdersBatchImportCompletedEvent>(e =>
                    e.JobRunId == _jobRunId &&
                    e.Created == 1 &&
                    e.Updated == 0 &&
                    e.Skipped == 0 &&
                    e.Failed == 0),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_ResolvesChannelProductId_WhenProjectionHasMatchingRef()
    {
        // Arrange
        string databaseName = Guid.NewGuid().ToString();
        Guid channelProductId = Guid.NewGuid();

        SalesDbContext setupContext = BuildSalesDbContext(_tenantId, databaseName);
        await setupContext.ChannelProductRefs.AddAsync(BuildChannelProductRef(channelProductId, "nm-100"));
        await setupContext.SaveChangesAsync();

        SalesDbContext consumerContext = BuildSalesDbContext(_tenantId, databaseName);
        Mock<IPublishEndpoint> publishEndpointMock = new Mock<IPublishEndpoint>();
        OrdersBatchImportRequestedConsumer consumer = BuildConsumer(consumerContext, publishEndpointMock.Object, _tenantId);

        // Act
        await consumer.ProcessAsync(
            BuildMessage(new List<ChannelOrderSnapshot> { BuildOrderSnapshot("ORDER-1", "nm-100", "SRID-1") }),
            CancellationToken.None);

        // Assert
        OrderLine? line = await consumerContext.OrderLines
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(l => l.LineKey == "SRID-1");

        line.Should().NotBeNull();
        line!.ChannelProductId.Should().Be(channelProductId);
    }

    [Fact]
    public async Task ProcessAsync_LeavesChannelProductIdNull_WhenProjectionHasNoMatch()
    {
        // Arrange
        string databaseName = Guid.NewGuid().ToString();

        SalesDbContext setupContext = BuildSalesDbContext(_tenantId, databaseName);
        await setupContext.ChannelProductRefs.AddAsync(BuildChannelProductRef(Guid.NewGuid(), "nm-other"));
        await setupContext.SaveChangesAsync();

        SalesDbContext consumerContext = BuildSalesDbContext(_tenantId, databaseName);
        Mock<IPublishEndpoint> publishEndpointMock = new Mock<IPublishEndpoint>();
        OrdersBatchImportRequestedConsumer consumer = BuildConsumer(consumerContext, publishEndpointMock.Object, _tenantId);

        // Act
        await consumer.ProcessAsync(
            BuildMessage(new List<ChannelOrderSnapshot> { BuildOrderSnapshot("ORDER-1", "nm-100", "SRID-1") }),
            CancellationToken.None);

        // Assert
        OrderLine? line = await consumerContext.OrderLines
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(l => l.LineKey == "SRID-1");

        line.Should().NotBeNull();
        line!.ChannelProductId.Should().BeNull();

        // The channel identity is retained regardless, so a later catalogue link can be backfilled.
        line.ExternalProductId.Should().Be("nm-100");
    }

    [Fact]
    public async Task ProcessAsync_ReimportingIdenticalOrder_CountsAsSkippedWithoutDuplicating()
    {
        // Arrange
        string databaseName = Guid.NewGuid().ToString();
        ChannelOrderSnapshot snapshot = BuildOrderSnapshot("ORDER-1", "nm-100", "SRID-1");

        SalesDbContext firstContext = BuildSalesDbContext(_tenantId, databaseName);
        Mock<IPublishEndpoint> firstPublishMock = new Mock<IPublishEndpoint>();
        OrdersBatchImportRequestedConsumer firstConsumer = BuildConsumer(firstContext, firstPublishMock.Object, _tenantId);
        await firstConsumer.ProcessAsync(BuildMessage(new List<ChannelOrderSnapshot> { snapshot }), CancellationToken.None);

        // Act: the same channel data arrives again
        SalesDbContext secondContext = BuildSalesDbContext(_tenantId, databaseName);
        Mock<IPublishEndpoint> secondPublishMock = new Mock<IPublishEndpoint>();
        OrdersBatchImportRequestedConsumer secondConsumer = BuildConsumer(secondContext, secondPublishMock.Object, _tenantId);
        await secondConsumer.ProcessAsync(BuildMessage(new List<ChannelOrderSnapshot> { snapshot }), CancellationToken.None);

        // Assert
        List<Order> orders = await secondContext.Orders.IgnoreQueryFilters().Include(o => o.Lines).ToListAsync();
        orders.Should().HaveCount(1);
        orders[0].Lines.Should().HaveCount(1);

        secondPublishMock.Verify(
            p => p.Publish(
                It.Is<OrdersBatchImportCompletedEvent>(e =>
                    e.Created == 0 &&
                    e.Updated == 0 &&
                    e.Skipped == 1 &&
                    e.Failed == 0),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_ChangedChannelData_CountsAsUpdatedAndOverwritesFields()
    {
        // Arrange
        string databaseName = Guid.NewGuid().ToString();

        SalesDbContext firstContext = BuildSalesDbContext(_tenantId, databaseName);
        Mock<IPublishEndpoint> firstPublishMock = new Mock<IPublishEndpoint>();
        OrdersBatchImportRequestedConsumer firstConsumer = BuildConsumer(firstContext, firstPublishMock.Object, _tenantId);
        await firstConsumer.ProcessAsync(
            BuildMessage(new List<ChannelOrderSnapshot> { BuildOrderSnapshot("ORDER-1", "nm-100", "SRID-1", price: 1500m) }),
            CancellationToken.None);

        // Act: the channel reports a new status and price for the same order
        SalesDbContext secondContext = BuildSalesDbContext(_tenantId, databaseName);
        Mock<IPublishEndpoint> secondPublishMock = new Mock<IPublishEndpoint>();
        OrdersBatchImportRequestedConsumer secondConsumer = BuildConsumer(secondContext, secondPublishMock.Object, _tenantId);

        ChannelOrderSnapshot changed = BuildOrderSnapshot("ORDER-1", "nm-100", "SRID-1", price: 999m);
        changed = changed with { ExternalStatus = "cancelled" };

        await secondConsumer.ProcessAsync(BuildMessage(new List<ChannelOrderSnapshot> { changed }), CancellationToken.None);

        // Assert
        Order? order = await secondContext.Orders
            .IgnoreQueryFilters()
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.ExternalOrderId == "ORDER-1");

        order.Should().NotBeNull();
        order!.Status.Should().Be(OrderStatus.Cancelled);
        order.ExternalStatus.Should().Be("cancelled");
        order.UpdatedAt.Should().NotBeNull();
        order.Lines.First().UnitPrice.Should().Be(999m);

        List<Order> allOrders = await secondContext.Orders.IgnoreQueryFilters().ToListAsync();
        allOrders.Should().HaveCount(1);

        secondPublishMock.Verify(
            p => p.Publish(
                It.Is<OrdersBatchImportCompletedEvent>(e =>
                    e.Created == 0 &&
                    e.Updated == 1 &&
                    e.Skipped == 0 &&
                    e.Failed == 0),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_NewLineInExistingOrder_CountsAsUpdated()
    {
        // Arrange
        string databaseName = Guid.NewGuid().ToString();

        SalesDbContext firstContext = BuildSalesDbContext(_tenantId, databaseName);
        Mock<IPublishEndpoint> firstPublishMock = new Mock<IPublishEndpoint>();
        OrdersBatchImportRequestedConsumer firstConsumer = BuildConsumer(firstContext, firstPublishMock.Object, _tenantId);
        await firstConsumer.ProcessAsync(
            BuildMessage(new List<ChannelOrderSnapshot> { BuildOrderSnapshot("ORDER-1", "nm-100", "SRID-1") }),
            CancellationToken.None);

        // Act: the same order now carries an extra line
        SalesDbContext secondContext = BuildSalesDbContext(_tenantId, databaseName);
        Mock<IPublishEndpoint> secondPublishMock = new Mock<IPublishEndpoint>();
        OrdersBatchImportRequestedConsumer secondConsumer = BuildConsumer(secondContext, secondPublishMock.Object, _tenantId);

        ChannelOrderSnapshot withExtraLine = BuildOrderSnapshot("ORDER-1", "nm-100", "SRID-1");
        withExtraLine.Lines.Add(new ChannelOrderLineSnapshot
        {
            LineKey = "SRID-2",
            ExternalProductId = "nm-200",
            Quantity = 1,
            Currency = "RUB",
        });

        await secondConsumer.ProcessAsync(BuildMessage(new List<ChannelOrderSnapshot> { withExtraLine }), CancellationToken.None);

        // Assert
        Order? order = await secondContext.Orders
            .IgnoreQueryFilters()
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.ExternalOrderId == "ORDER-1");

        order.Should().NotBeNull();
        order!.Lines.Should().HaveCount(2);

        secondPublishMock.Verify(
            p => p.Publish(
                It.Is<OrdersBatchImportCompletedEvent>(e => e.Updated == 1 && e.Skipped == 0),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_MultipleOrders_ReportsIndependentCounters()
    {
        // Arrange
        string databaseName = Guid.NewGuid().ToString();

        SalesDbContext seedContext = BuildSalesDbContext(_tenantId, databaseName);
        Mock<IPublishEndpoint> seedPublishMock = new Mock<IPublishEndpoint>();
        OrdersBatchImportRequestedConsumer seedConsumer = BuildConsumer(seedContext, seedPublishMock.Object, _tenantId);
        await seedConsumer.ProcessAsync(
            BuildMessage(new List<ChannelOrderSnapshot> { BuildOrderSnapshot("ORDER-EXISTING", "nm-100", "SRID-1") }),
            CancellationToken.None);

        SalesDbContext consumerContext = BuildSalesDbContext(_tenantId, databaseName);
        Mock<IPublishEndpoint> publishEndpointMock = new Mock<IPublishEndpoint>();
        OrdersBatchImportRequestedConsumer consumer = BuildConsumer(consumerContext, publishEndpointMock.Object, _tenantId);

        // Act: one unchanged order, one genuinely new order
        await consumer.ProcessAsync(
            BuildMessage(new List<ChannelOrderSnapshot>
            {
                BuildOrderSnapshot("ORDER-EXISTING", "nm-100", "SRID-1"),
                BuildOrderSnapshot("ORDER-NEW", "nm-500", "SRID-9"),
            }),
            CancellationToken.None);

        // Assert
        publishEndpointMock.Verify(
            p => p.Publish(
                It.Is<OrdersBatchImportCompletedEvent>(e =>
                    e.Created == 1 &&
                    e.Skipped == 1 &&
                    e.Updated == 0 &&
                    e.Failed == 0),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_OneFailingOrder_DoesNotAbortTheBatch()
    {
        // Arrange
        string databaseName = Guid.NewGuid().ToString();

        SalesDbContext consumerContext = BuildSalesDbContext(_tenantId, databaseName, failOnSaveCall: 1);
        Mock<IPublishEndpoint> publishEndpointMock = new Mock<IPublishEndpoint>();
        OrdersBatchImportRequestedConsumer consumer = BuildConsumer(consumerContext, publishEndpointMock.Object, _tenantId);

        // Act
        await consumer.ProcessAsync(
            BuildMessage(new List<ChannelOrderSnapshot>
            {
                BuildOrderSnapshot("ORDER-FAIL", "nm-100", "SRID-1"),
                BuildOrderSnapshot("ORDER-OK", "nm-200", "SRID-2"),
            }),
            CancellationToken.None);

        // Assert
        publishEndpointMock.Verify(
            p => p.Publish(
                It.Is<OrdersBatchImportCompletedEvent>(e =>
                    e.Failed == 1 &&
                    e.Created == 1),
                It.IsAny<CancellationToken>()),
            Times.Once);

        // The poisoned order must have been detached, not partially flushed alongside its sibling.
        SalesDbContext verificationContext = BuildSalesDbContext(_tenantId, databaseName);
        List<Order> orders = await verificationContext.Orders.IgnoreQueryFilters().ToListAsync();
        orders.Should().HaveCount(1);
        orders[0].ExternalOrderId.Should().Be("ORDER-OK");
    }

    [Fact]
    public async Task ProcessAsync_OrderOfAnotherTenant_IsNotVisibleThroughQueryFilter()
    {
        // Arrange
        string databaseName = Guid.NewGuid().ToString();
        Guid otherTenantId = Guid.NewGuid();

        SalesDbContext otherTenantContext = BuildSalesDbContext(otherTenantId, databaseName);
        Mock<IPublishEndpoint> otherTenantPublishMock = new Mock<IPublishEndpoint>();
        OrdersBatchImportRequestedConsumer otherTenantConsumer = BuildConsumer(otherTenantContext, otherTenantPublishMock.Object, otherTenantId);
        await otherTenantConsumer.ProcessAsync(
            BuildMessage(new List<ChannelOrderSnapshot> { BuildOrderSnapshot("ORDER-SHARED", "nm-100", "SRID-1") }),
            CancellationToken.None);

        SalesDbContext currentTenantContext = BuildSalesDbContext(_tenantId, databaseName);
        Mock<IPublishEndpoint> currentTenantPublishMock = new Mock<IPublishEndpoint>();
        OrdersBatchImportRequestedConsumer currentTenantConsumer = BuildConsumer(currentTenantContext, currentTenantPublishMock.Object, _tenantId);

        // Act: the current tenant imports an order with the very same channel-side identifier
        await currentTenantConsumer.ProcessAsync(
            BuildMessage(new List<ChannelOrderSnapshot> { BuildOrderSnapshot("ORDER-SHARED", "nm-100", "SRID-1") }),
            CancellationToken.None);

        // Assert: the two rows coexist, one per tenant
        List<Order> allOrders = await currentTenantContext.Orders.IgnoreQueryFilters().ToListAsync();
        allOrders.Should().HaveCount(2);

        List<Order> visibleOrders = await currentTenantContext.Orders.ToListAsync();
        visibleOrders.Should().HaveCount(1);
        visibleOrders[0].ExternalOrderId.Should().Be("ORDER-SHARED");

        // The second tenant's import created its own row rather than updating the first tenant's.
        currentTenantPublishMock.Verify(
            p => p.Publish(
                It.Is<OrdersBatchImportCompletedEvent>(e => e.Created == 1 && e.Updated == 0),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_TenantScopedRows_ReceiveTheContextTenantId()
    {
        // Arrange
        string databaseName = Guid.NewGuid().ToString();
        SalesDbContext consumerContext = BuildSalesDbContext(_tenantId, databaseName);

        Mock<IPublishEndpoint> publishEndpointMock = new Mock<IPublishEndpoint>();
        OrdersBatchImportRequestedConsumer consumer = BuildConsumer(consumerContext, publishEndpointMock.Object, _tenantId);

        // Act
        await consumer.ProcessAsync(
            BuildMessage(new List<ChannelOrderSnapshot> { BuildOrderSnapshot("ORDER-1", "nm-100", "SRID-1") }),
            CancellationToken.None);

        // Assert: the shadow TenantId is populated on every written row, order and line alike.
        Order order = await consumerContext.Orders.IgnoreQueryFilters().FirstAsync();
        object orderTenantId = consumerContext.Entry(order).Property("TenantId").CurrentValue!;
        orderTenantId.Should().Be(_tenantId);

        OrderLine line = await consumerContext.OrderLines.IgnoreQueryFilters().FirstAsync();
        object lineTenantId = consumerContext.Entry(line).Property("TenantId").CurrentValue!;
        lineTenantId.Should().Be(_tenantId);
    }

    [Fact]
    public async Task ProcessAsync_EmptyBatch_PublishesZeroCounters()
    {
        // Arrange
        string databaseName = Guid.NewGuid().ToString();
        SalesDbContext consumerContext = BuildSalesDbContext(_tenantId, databaseName);

        Mock<IPublishEndpoint> publishEndpointMock = new Mock<IPublishEndpoint>();
        OrdersBatchImportRequestedConsumer consumer = BuildConsumer(consumerContext, publishEndpointMock.Object, _tenantId);

        // Act
        await consumer.ProcessAsync(BuildMessage(new List<ChannelOrderSnapshot>()), CancellationToken.None);

        // Assert
        publishEndpointMock.Verify(
            p => p.Publish(
                It.Is<OrdersBatchImportCompletedEvent>(e =>
                    e.Created == 0 &&
                    e.Updated == 0 &&
                    e.Skipped == 0 &&
                    e.Failed == 0 &&
                    e.JobRunId == _jobRunId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_IsTerminalBatch_PropagatesBatchIdUnchanged()
    {
        // Arrange
        string databaseName = Guid.NewGuid().ToString();
        SalesDbContext consumerContext = BuildSalesDbContext(_tenantId, databaseName);

        Mock<IPublishEndpoint> publishEndpointMock = new Mock<IPublishEndpoint>();
        OrdersBatchImportRequestedConsumer consumer = BuildConsumer(consumerContext, publishEndpointMock.Object, _tenantId);

        Guid batchId = Guid.NewGuid();

        OrdersBatchImportRequestedEvent message = new OrdersBatchImportRequestedEvent
        {
            JobRunId = _jobRunId,
            ConnectionId = _connectionId,
            ChannelId = _channelId,
            BatchId = batchId,
            Orders = new List<ChannelOrderSnapshot> { BuildOrderSnapshot("ORDER-1", "nm-100", "SRID-1") },
            IsTerminal = true,
        };

        // Act
        await consumer.ProcessAsync(message, CancellationToken.None);

        // Assert: the aggregator deduplicates by BatchId, so it must survive the round trip.
        publishEndpointMock.Verify(
            p => p.Publish(
                It.Is<OrdersBatchImportCompletedEvent>(e => e.BatchId == batchId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private OrdersBatchImportRequestedConsumer BuildConsumer(
        SalesDbContext context,
        IPublishEndpoint publishEndpoint,
        Guid tenantId)
    {
        Mock<ILogger<OrdersBatchImportRequestedConsumer>> loggerMock = new Mock<ILogger<OrdersBatchImportRequestedConsumer>>();
        Mock<ITenantContext> tenantContextMock = new Mock<ITenantContext>();
        tenantContextMock.Setup(tc => tc.TenantId).Returns(tenantId);

        return new OrdersBatchImportRequestedConsumer(context, publishEndpoint, tenantContextMock.Object, loggerMock.Object);
    }

    private SalesDbContext BuildSalesDbContext(Guid tenantId, string databaseName, int failOnSaveCall = 0)
    {
        DbContextOptions<SalesDbContext> options = new DbContextOptionsBuilder<SalesDbContext>()
            .UseInMemoryDatabase(databaseName: databaseName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        Mock<ITenantContext> tenantContextMock = new Mock<ITenantContext>();
        tenantContextMock.Setup(tc => tc.TenantId).Returns(tenantId);

        if (failOnSaveCall > 0)
        {
            return new FailingSalesDbContext(options, tenantContextMock.Object, failOnSaveCall);
        }

        return new SalesDbContext(options, tenantContextMock.Object);
    }

    private OrdersBatchImportRequestedEvent BuildMessage(List<ChannelOrderSnapshot> orders)
    {
        return new OrdersBatchImportRequestedEvent
        {
            JobRunId = _jobRunId,
            ConnectionId = _connectionId,
            ChannelId = _channelId,
            BatchId = Guid.NewGuid(),
            Orders = orders,
            IsTerminal = true,
        };
    }

    // Fixed rather than DateTimeOffset.UtcNow: two BuildOrderSnapshot calls for what a test
    // considers "the same channel data" must produce byte-identical snapshots, otherwise the
    // consumer's change detection correctly reports Updated instead of the Skipped the test expects.
    private static readonly DateTimeOffset FixedOrderDate = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset FixedStatusChangedAt = new DateTimeOffset(2026, 1, 1, 11, 0, 0, TimeSpan.Zero);

    private static ChannelOrderSnapshot BuildOrderSnapshot(
        string externalOrderId,
        string externalProductId,
        string lineKey,
        decimal price = 1500m)
    {
        return new ChannelOrderSnapshot
        {
            ExternalOrderId = externalOrderId,
            ExternalOrderNumber = externalOrderId,
            OrderDate = FixedOrderDate,
            StatusChangedAt = FixedStatusChangedAt,
            ExternalStatus = "active",
            ExternalSubstatus = "normal",
            Currency = "RUB",
            DeliveryRegion = "Moscow",
            WarehouseName = "Warehouse 1",
            IsSellerWarehouse = true,
            IsB2b = false,
            IsTestOrder = false,
            ChannelReportedTotal = 1700m,
            Lines = new List<ChannelOrderLineSnapshot>
            {
                new ChannelOrderLineSnapshot
                {
                    LineKey = lineKey,
                    ExternalLineId = lineKey,
                    ExternalProductId = externalProductId,
                    OfferId = "SKU-" + externalProductId,
                    ProductName = "Test Product",
                    Quantity = 1,
                    UnitPrice = price,
                    BuyerUnitPrice = price,
                    UnitPriceBeforeDiscount = price + 200m,
                    Currency = "RUB",
                },
            },
        };
    }

    private ChannelProductRef BuildChannelProductRef(Guid channelProductId, string externalProductId)
    {
        return new ChannelProductRef
        {
            ChannelProductId = channelProductId,
            ProductId = Guid.NewGuid(),
            ChannelId = _channelId,
            ExternalProductId = externalProductId,
            ExternalSku = "SKU-" + externalProductId,
            ProductName = "Test Product",
            IsActive = true,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>
    /// Makes the Nth SaveChangesAsync throw, so the per-order failure containment can be observed
    /// deterministically. A real SaveChanges failure cannot be provoked through the InMemory
    /// provider, which enforces neither constraints nor transactions.
    /// </summary>
    private sealed class FailingSalesDbContext : SalesDbContext
    {
        private readonly int _failOnCall;
        private int _saveCalls;

        public FailingSalesDbContext(
            DbContextOptions<SalesDbContext> options,
            ITenantContext tenantContext,
            int failOnCall)
            : base(options, tenantContext)
        {
            _failOnCall = failOnCall;
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            _saveCalls++;

            if (_saveCalls == _failOnCall)
            {
                throw new InvalidOperationException("Simulated persistence failure for a single order.");
            }

            return base.SaveChangesAsync(cancellationToken);
        }
    }
}
