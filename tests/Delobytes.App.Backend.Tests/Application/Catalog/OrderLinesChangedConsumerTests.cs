using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Catalog.Application.Interfaces;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Application.Services.CostCalculation;
using Delobytes.App.Backend.Catalog.Contracts.Events;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Infrastructure.Messaging.Consumers;
using Delobytes.App.Backend.Contracts.Accounting;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Sales.Contracts.Events;
using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Catalog;

/// <summary>
/// Tests for the Catalog consumer that turns Sales order lines into production cost results. The
/// as-of date is tenant-local because Sales stores no shipment date, and the channel product may be
/// resolved by identifier or by the natural key.
/// </summary>
public class OrderLinesChangedConsumerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ChannelId = Guid.NewGuid();
    private static readonly Guid ProductId = Guid.NewGuid();

    [Fact]
    public async Task Consume_LineWithChannelProductId_ResolvesDirectly()
    {
        RecordingCostCalculator calculator = new RecordingCostCalculator(unitTotal: 10m);

        OrderLineCostsResolvedEvent? published = await ConsumeAsync(
            calculator,
            new Dictionary<(Guid, string), Guid>(),
            line(channelProductId: Guid.NewGuid(), quantity: 1));

        published!.Results.Should().ContainSingle();
        published!.Results[0].Status.Should().Be(OrderLineCostStatus.Resolved);
        published!.Results[0].ProductId.Should().Be(ProductId == Guid.Empty ? Guid.Empty : calculator.LastProductId);
        calculator.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Consume_LineWithoutChannelProductId_FallsBackToTheNaturalKey()
    {
        RecordingCostCalculator calculator = new RecordingCostCalculator(unitTotal: 10m);
        Dictionary<(Guid, string), Guid> links = new() { [(ChannelId, "sku-1")] = ProductId };

        OrderLineCostsResolvedEvent? published = await ConsumeAsync(
            calculator,
            links,
            line(channelProductId: null, quantity: 1));

        published!.Results[0].Status.Should().Be(OrderLineCostStatus.Resolved);
        published!.Results[0].ProductId.Should().Be(ProductId);
    }

    [Fact]
    public async Task Consume_UnknownProduct_ReportsNotLinkedWithoutCalculating()
    {
        RecordingCostCalculator calculator = new RecordingCostCalculator(unitTotal: 10m);

        OrderLineCostsResolvedEvent? published = await ConsumeAsync(
            calculator,
            new Dictionary<(Guid, string), Guid>(),
            line(channelProductId: null, quantity: 1));

        published!.Results[0].Status.Should().Be(OrderLineCostStatus.ProductNotLinked);
        published!.Results[0].ProductId.Should().BeNull();
        published!.Results[0].IsComplete.Should().BeFalse();
        calculator.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Consume_ArchivedLink_StillResolves()
    {
        // The repository is not filtered on IsActive: an archived card owns historical lines.
        RecordingCostCalculator calculator = new RecordingCostCalculator(unitTotal: 10m);
        Dictionary<(Guid, string), Guid> links = new() { [(ChannelId, "sku-1")] = ProductId };

        OrderLineCostsResolvedEvent? published = await ConsumeAsync(
            calculator,
            links,
            line(channelProductId: null, quantity: 1));

        published!.Results[0].Status.Should().Be(OrderLineCostStatus.Resolved);
    }

    [Fact]
    public async Task Consume_OrderDateNearUtcMidnight_UsesTheTenantLocalDate()
    {
        // 22:30 UTC is the next calendar day in Moscow.
        RecordingCostCalculator calculator = new RecordingCostCalculator(unitTotal: 10m);

        OrderLineCostsResolvedEvent? published = await ConsumeAsync(
            calculator,
            new Dictionary<(Guid, string), Guid>(),
            new[]
            {
                line(
                    channelProductId: Guid.NewGuid(),
                    quantity: 1,
                    orderDate: new DateTimeOffset(2026, 8, 1, 22, 30, 0, TimeSpan.Zero)),
            },
            TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow"));

        published!.Results[0].AsOfDate.Should().Be(new DateOnly(2026, 8, 2));
        calculator.LastAsOf.Should().Be(new DateOnly(2026, 8, 2));
    }

    [Fact]
    public async Task Consume_TwoLinesOfTheSameProductAndDate_CalculatesOnceAndMultipliesQuantity()
    {
        RecordingCostCalculator calculator = new RecordingCostCalculator(unitTotal: 10m);
        Guid channelProductId = Guid.NewGuid();

        OrderLineCostsResolvedEvent? published = await ConsumeAsync(
            calculator,
            new Dictionary<(Guid, string), Guid>(),
            line(channelProductId: channelProductId, quantity: 1),
            line(channelProductId: channelProductId, quantity: 3));

        calculator.Calls.Should().Be(1);
        published!.Results.Should().HaveCount(2);
        published!.Results[1].TotalCost.Should().Be(30m);
    }

    [Fact]
    public async Task Consume_IncompleteBreakdown_IsPassedThrough()
    {
        RecordingCostCalculator calculator = new RecordingCostCalculator(
            unitTotal: 10m,
            warnings: new[] { new CostWarning(CostWarningType.MissingBom, "no bom", null) });

        OrderLineCostsResolvedEvent? published = await ConsumeAsync(
            calculator,
            new Dictionary<(Guid, string), Guid>(),
            line(channelProductId: Guid.NewGuid(), quantity: 1));

        published!.Results[0].IsComplete.Should().BeFalse();
        published!.Results[0].WarningTypes.Should().Contain(nameof(CostWarningType.MissingBom));
    }

    [Fact]
    public async Task Consume_LineWithoutCostRelevantChange_IsIgnored()
    {
        RecordingCostCalculator calculator = new RecordingCostCalculator(unitTotal: 10m);

        ChangedOrderLine unchanged = line(channelProductId: Guid.NewGuid(), quantity: 1);
        unchanged = unchanged with { CostRelevantChange = false };

        OrderLineCostsResolvedEvent? published = await ConsumeAsync(
            calculator,
            new Dictionary<(Guid, string), Guid>(),
            unchanged);

        published.Should().BeNull();
        calculator.Calls.Should().Be(0);
    }

    private static async Task<OrderLineCostsResolvedEvent?> ConsumeAsync(
        RecordingCostCalculator calculator,
        Dictionary<(Guid, string), Guid> links,
        params ChangedOrderLine[] lines)
    {
        return await ConsumeAsync(calculator, links, lines, TimeZoneInfo.Utc);
    }

    private static async Task<OrderLineCostsResolvedEvent?> ConsumeAsync(
        RecordingCostCalculator calculator,
        Dictionary<(Guid, string), Guid> links,
        ChangedOrderLine[] lines,
        TimeZoneInfo timeZone)
    {
        Mock<ITenantContext> tenantContext = new Mock<ITenantContext>();
        tenantContext.Setup(t => t.TenantId).Returns(TenantId);

        Mock<ITenantTimeService> timeService = new Mock<ITenantTimeService>();
        timeService
            .Setup(t => t.GetTimeZoneAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(timeZone);

        RecordingPublisher publisher = new RecordingPublisher();

        OrderLinesChangedConsumer consumer = new OrderLinesChangedConsumer(
            new FakeChannelProductRepository(links),
            calculator,
            timeService.Object,
            publisher,
            tenantContext.Object,
            TimeProvider.System,
            NullLogger<OrderLinesChangedConsumer>.Instance);

        Mock<ConsumeContext<OrderLinesChangedEvent>> context = new Mock<ConsumeContext<OrderLinesChangedEvent>>();
        context.SetupGet(c => c.Message).Returns(new OrderLinesChangedEvent { Lines = lines.ToList() });
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);

        await consumer.Consume(context.Object);

        return publisher.Events.OfType<OrderLineCostsResolvedEvent>().SingleOrDefault();
    }

    private static ChangedOrderLine line(Guid? channelProductId, int quantity, DateTimeOffset? orderDate = null)
    {
        return new ChangedOrderLine
        {
            OrderLineId = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            ChannelId = ChannelId,
            ExternalOrderId = "order-1",
            ExternalLineId = "srid-1",
            ExternalProductId = "sku-1",
            ChannelProductId = channelProductId,
            Quantity = quantity,
            OrderDate = orderDate ?? new DateTimeOffset(2026, 8, 1, 10, 0, 0, TimeSpan.Zero),
            Currency = "RUB",
            IsNew = true,
            CostRelevantChange = true,
        };
    }

    private sealed class RecordingCostCalculator : ICostCalculator
    {
        private readonly decimal _unitTotal;
        private readonly IReadOnlyList<CostWarning> _warnings;

        public RecordingCostCalculator(decimal unitTotal, IReadOnlyList<CostWarning>? warnings = null)
        {
            _unitTotal = unitTotal;
            _warnings = warnings ?? Array.Empty<CostWarning>();
        }

        public int Calls { get; private set; }

        public Guid LastProductId { get; private set; }

        public DateOnly LastAsOf { get; private set; }

        public Task<CostBreakdown> CalculateAsync(Guid productId, DateOnly asOf, CancellationToken ct)
        {
            Calls++;
            LastProductId = productId;
            LastAsOf = asOf;

            return Task.FromResult(new CostBreakdown(
                productId,
                asOf,
                1m,
                2m,
                3m,
                4m,
                _unitTotal,
                Array.Empty<CostLine>(),
                _warnings));
        }

        public Task<CostBreakdown> CalculateForLinesAsync(
            Guid productId,
            DateOnly asOf,
            IReadOnlyList<CostCalculationLine> lines,
            CancellationToken ct)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakeChannelProductRepository : IChannelProductRepository
    {
        private readonly Dictionary<(Guid, string), Guid> _links;

        public FakeChannelProductRepository(Dictionary<(Guid, string), Guid> links)
        {
            _links = links;
        }

        public Task<IReadOnlyList<ChannelProduct>> GetAbandonedLinksAsync(
            Guid channelId,
            DateTimeOffset syncedBefore,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<ChannelProduct>>(Array.Empty<ChannelProduct>());
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<Guid?> FindChannelProductIdAsync(
            Guid channelId,
            string externalProductId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(
                _links.TryGetValue((channelId, externalProductId), out Guid id) ? (Guid?)id : null);
        }
    }

    private sealed class RecordingPublisher : IEventPublisher
    {
        public List<object> Events { get; } = new List<object>();

        public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken)
            where TEvent : class
        {
            Events.Add(@event);
            return Task.CompletedTask;
        }
    }
}


