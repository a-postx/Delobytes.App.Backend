using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Sales.Application.Models;
using Delobytes.App.Backend.Sales.Domain.Entities;
using Delobytes.App.Backend.Sales.Domain.Enums;
using Delobytes.App.Backend.Sales.Infrastructure.Persistence;
using Delobytes.App.Backend.Sales.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Infrastructure.Sales;

/// <summary>
/// OrderStatsRepository against a seeded SalesDbContext. The contract: only surviving lines of
/// the current tenant, in the tenant currency on both the order and the line, inside the
/// half-open UTC window, optionally narrowed by channel. Tenant isolation here must come from
/// the context's global query filter, not from a hand-written predicate.
/// </summary>
public class OrderStatsRepositoryTests
{
    private const string Currency = "RUB";

    private static readonly DateTimeOffset WindowFromUtc = new(2026, 1, 4, 14, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset WindowToUtcExclusive = new(2026, 1, 11, 14, 0, 0, TimeSpan.Zero);

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _channelId = Guid.NewGuid();
    private readonly Guid _otherChannelId = Guid.NewGuid();

    [Fact]
    public async Task GetLineFactsAsync_LineOfAnotherTenant_IsNeverReturned()
    {
        // Arrange
        string databaseName = Guid.NewGuid().ToString();
        Guid otherTenantId = Guid.NewGuid();

        SalesDbContext otherTenantContext = BuildContext(otherTenantId, databaseName);
        otherTenantContext.Orders.Add(BuildOrder(_channelId, new DateTimeOffset(2026, 1, 6, 12, 0, 0, TimeSpan.Zero)));
        await otherTenantContext.SaveChangesAsync();

        SalesDbContext currentTenantContext = BuildContext(_tenantId, databaseName);
        currentTenantContext.Orders.Add(BuildOrder(_channelId, new DateTimeOffset(2026, 1, 7, 12, 0, 0, TimeSpan.Zero)));
        await currentTenantContext.SaveChangesAsync();

        OrderStatsRepository repository = new(currentTenantContext);

        // Act
        IReadOnlyList<OrderLineFact> facts = await repository.GetLineFactsAsync(
            WindowFromUtc,
            WindowToUtcExclusive,
            Currency,
            null,
            CancellationToken.None);

        // Assert
        facts.Should().ContainSingle();

        // The other tenant's row still exists; it is only invisible through the query filter.
        List<Order> allOrders = await currentTenantContext.Orders.IgnoreQueryFilters().ToListAsync();
        allOrders.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetLineFactsAsync_CancelledLine_IsExcluded()
    {
        // Arrange
        string databaseName = Guid.NewGuid().ToString();
        SalesDbContext setupContext = BuildContext(_tenantId, databaseName);

        setupContext.Orders.Add(BuildOrder(
            _channelId,
            new DateTimeOffset(2026, 1, 6, 12, 0, 0, TimeSpan.Zero),
            lineIsCancelled: true));
        await setupContext.SaveChangesAsync();

        OrderStatsRepository repository = new(BuildContext(_tenantId, databaseName));

        // Act
        IReadOnlyList<OrderLineFact> facts = await repository.GetLineFactsAsync(
            WindowFromUtc,
            WindowToUtcExclusive,
            Currency,
            null,
            CancellationToken.None);

        // Assert
        facts.Should().BeEmpty();
    }

    [Fact]
    public async Task GetLineFactsAsync_LineInDifferentCurrency_IsExcluded()
    {
        // Arrange: the order matches the tenant currency, only the line does not.
        string databaseName = Guid.NewGuid().ToString();
        SalesDbContext setupContext = BuildContext(_tenantId, databaseName);

        setupContext.Orders.Add(BuildOrder(
            _channelId,
            new DateTimeOffset(2026, 1, 6, 12, 0, 0, TimeSpan.Zero),
            lineCurrency: "USD"));
        await setupContext.SaveChangesAsync();

        OrderStatsRepository repository = new(BuildContext(_tenantId, databaseName));

        // Act
        IReadOnlyList<OrderLineFact> facts = await repository.GetLineFactsAsync(
            WindowFromUtc,
            WindowToUtcExclusive,
            Currency,
            null,
            CancellationToken.None);

        // Assert
        facts.Should().BeEmpty();
    }

    [Fact]
    public async Task GetLineFactsAsync_OrderInDifferentCurrency_IsExcluded()
    {
        // Arrange: the line matches the tenant currency, only the order does not.
        string databaseName = Guid.NewGuid().ToString();
        SalesDbContext setupContext = BuildContext(_tenantId, databaseName);

        setupContext.Orders.Add(BuildOrder(
            _channelId,
            new DateTimeOffset(2026, 1, 6, 12, 0, 0, TimeSpan.Zero),
            orderCurrency: "USD"));
        await setupContext.SaveChangesAsync();

        OrderStatsRepository repository = new(BuildContext(_tenantId, databaseName));

        // Act
        IReadOnlyList<OrderLineFact> facts = await repository.GetLineFactsAsync(
            WindowFromUtc,
            WindowToUtcExclusive,
            Currency,
            null,
            CancellationToken.None);

        // Assert
        facts.Should().BeEmpty();
    }

    [Fact]
    public async Task GetLineFactsAsync_ChannelSupplied_NarrowsToThatChannelOnly()
    {
        // Arrange
        string databaseName = Guid.NewGuid().ToString();
        SalesDbContext setupContext = BuildContext(_tenantId, databaseName);

        setupContext.Orders.Add(BuildOrder(
            _channelId,
            new DateTimeOffset(2026, 1, 6, 12, 0, 0, TimeSpan.Zero),
            unitPrice: 100m));
        setupContext.Orders.Add(BuildOrder(
            _otherChannelId,
            new DateTimeOffset(2026, 1, 7, 12, 0, 0, TimeSpan.Zero),
            unitPrice: 200m));
        await setupContext.SaveChangesAsync();

        OrderStatsRepository repository = new(BuildContext(_tenantId, databaseName));

        // Act
        IReadOnlyList<OrderLineFact> facts = await repository.GetLineFactsAsync(
            WindowFromUtc,
            WindowToUtcExclusive,
            Currency,
            _channelId,
            CancellationToken.None);

        // Assert
        facts.Should().ContainSingle().Which.UnitPrice.Should().Be(100m);
    }

    [Fact]
    public async Task GetLineFactsAsync_ChannelOmitted_ReturnsAllChannels()
    {
        // Arrange
        string databaseName = Guid.NewGuid().ToString();
        SalesDbContext setupContext = BuildContext(_tenantId, databaseName);

        setupContext.Orders.Add(BuildOrder(
            _channelId,
            new DateTimeOffset(2026, 1, 6, 12, 0, 0, TimeSpan.Zero),
            unitPrice: 100m));
        setupContext.Orders.Add(BuildOrder(
            _otherChannelId,
            new DateTimeOffset(2026, 1, 7, 12, 0, 0, TimeSpan.Zero),
            unitPrice: 200m));
        await setupContext.SaveChangesAsync();

        OrderStatsRepository repository = new(BuildContext(_tenantId, databaseName));

        // Act
        IReadOnlyList<OrderLineFact> facts = await repository.GetLineFactsAsync(
            WindowFromUtc,
            WindowToUtcExclusive,
            Currency,
            null,
            CancellationToken.None);

        // Assert
        facts.Select(fact => fact.UnitPrice).Should().BeEquivalentTo(new decimal?[] { 100m, 200m });
    }

    [Fact]
    public async Task GetLineFactsAsync_OrderDateExactlyAtFromUtc_IsIncluded()
    {
        // Arrange: the lower bound is inclusive.
        string databaseName = Guid.NewGuid().ToString();
        SalesDbContext setupContext = BuildContext(_tenantId, databaseName);

        setupContext.Orders.Add(BuildOrder(_channelId, WindowFromUtc));
        await setupContext.SaveChangesAsync();

        OrderStatsRepository repository = new(BuildContext(_tenantId, databaseName));

        // Act
        IReadOnlyList<OrderLineFact> facts = await repository.GetLineFactsAsync(
            WindowFromUtc,
            WindowToUtcExclusive,
            Currency,
            null,
            CancellationToken.None);

        // Assert
        facts.Should().ContainSingle().Which.OrderDate.Should().Be(WindowFromUtc);
    }

    [Fact]
    public async Task GetLineFactsAsync_OrderDateExactlyAtToUtcExclusive_IsExcluded()
    {
        // Arrange: the upper bound is exclusive, so the first instant outside the window drops out.
        string databaseName = Guid.NewGuid().ToString();
        SalesDbContext setupContext = BuildContext(_tenantId, databaseName);

        setupContext.Orders.Add(BuildOrder(_channelId, WindowToUtcExclusive));
        await setupContext.SaveChangesAsync();

        OrderStatsRepository repository = new(BuildContext(_tenantId, databaseName));

        // Act
        IReadOnlyList<OrderLineFact> facts = await repository.GetLineFactsAsync(
            WindowFromUtc,
            WindowToUtcExclusive,
            Currency,
            null,
            CancellationToken.None);

        // Assert
        facts.Should().BeEmpty();
    }

    [Fact]
    public async Task GetLineFactsAsync_OrderBeforeWindow_IsExcluded()
    {
        // Arrange
        string databaseName = Guid.NewGuid().ToString();
        SalesDbContext setupContext = BuildContext(_tenantId, databaseName);

        setupContext.Orders.Add(BuildOrder(_channelId, WindowFromUtc.AddTicks(-1)));
        await setupContext.SaveChangesAsync();

        OrderStatsRepository repository = new(BuildContext(_tenantId, databaseName));

        // Act
        IReadOnlyList<OrderLineFact> facts = await repository.GetLineFactsAsync(
            WindowFromUtc,
            WindowToUtcExclusive,
            Currency,
            null,
            CancellationToken.None);

        // Assert
        facts.Should().BeEmpty();
    }

    [Fact]
    public async Task GetLineFactsAsync_MatchingLine_ProjectsOnlyTheFactsWithNoTracking()
    {
        // Arrange
        string databaseName = Guid.NewGuid().ToString();
        SalesDbContext setupContext = BuildContext(_tenantId, databaseName);

        DateTimeOffset orderDate = new(2026, 1, 6, 12, 0, 0, TimeSpan.Zero);
        Order order = BuildOrder(_channelId, orderDate, quantity: 3, unitPrice: 149.99m);
        setupContext.Orders.Add(order);
        await setupContext.SaveChangesAsync();

        SalesDbContext readContext = BuildContext(_tenantId, databaseName);
        OrderStatsRepository repository = new(readContext);

        // Act
        IReadOnlyList<OrderLineFact> facts = await repository.GetLineFactsAsync(
            WindowFromUtc,
            WindowToUtcExclusive,
            Currency,
            null,
            CancellationToken.None);

        // Assert
        facts.Should().ContainSingle();
        facts[0].OrderId.Should().Be(order.Id);
        facts[0].OrderDate.Should().Be(orderDate);
        facts[0].Quantity.Should().Be(3);
        facts[0].UnitPrice.Should().Be(149.99m);

        readContext.ChangeTracker.Entries().Should().BeEmpty();
    }

    private SalesDbContext BuildContext(Guid tenantId, string databaseName)
    {
        DbContextOptions<SalesDbContext> options = new DbContextOptionsBuilder<SalesDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        Mock<ITenantContext> tenantContextMock = new();
        tenantContextMock.Setup(t => t.TenantId).Returns(tenantId);

        return new SalesDbContext(options, tenantContextMock.Object);
    }

    private Order BuildOrder(
        Guid channelId,
        DateTimeOffset orderDate,
        bool lineIsCancelled = false,
        string lineCurrency = Currency,
        string orderCurrency = Currency,
        int quantity = 1,
        decimal? unitPrice = 1500m)
    {
        OrderLine line = new()
        {
            Id = Guid.NewGuid(),
            LineKey = Guid.NewGuid().ToString(),
            ExternalProductId = "nm-100",
            Quantity = quantity,
            UnitPrice = unitPrice,
            Currency = lineCurrency,
            IsCancelled = lineIsCancelled,
            CreatedAt = orderDate,
        };

        Order order = new()
        {
            Id = Guid.NewGuid(),
            ChannelId = channelId,
            ExternalOrderId = Guid.NewGuid().ToString(),
            OrderDate = orderDate,
            Status = OrderStatus.Processing,
            ExternalStatus = "active",
            Currency = orderCurrency,
            FirstImportedAt = orderDate,
            LastImportedAt = orderDate,
            CreatedAt = orderDate,
        };

        order.Lines.Add(line);

        return order;
    }
}
