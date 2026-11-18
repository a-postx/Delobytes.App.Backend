using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Sales.Application.Models;
using Delobytes.App.Backend.Sales.Domain.Entities;
using Delobytes.App.Backend.Sales.Infrastructure.Persistence;
using Delobytes.App.Backend.Sales.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Infrastructure.Sales;

/// <summary>
/// Behaviour of the re-publish page source against real DbContexts: the join to the order, the
/// narrowing and the keyset must page deterministically over the tenant's own lines.
/// </summary>
public class OrderLineRepublishRepositoryTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherTenantId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ChannelId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherChannelId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static readonly DateTimeOffset Day1 = new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Day2 = new(2026, 8, 2, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Day3 = new(2026, 8, 3, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetPageAsync_PagesThroughEveryRowOnce()
    {
        string database = NewDatabaseName();
        Guid[] lineIds = await SeedAsync(database);

        using SalesDbContext context = CreateContext(database, TenantId);
        OrderLineRepublishRepository repository = new OrderLineRepublishRepository(context);

        IReadOnlyList<RepublishOrderLine> first = await repository.GetPageAsync(
            null, null, null, null, 2, CancellationToken.None);

        first.Should().HaveCount(2);

        string token = first[first.Count - 1].OrderLineId.ToString();

        IReadOnlyList<RepublishOrderLine> second = await repository.GetPageAsync(
            null, null, null, token, 2, CancellationToken.None);

        second.Should().HaveCount(1);

        first.Select(row => row.OrderLineId)
            .Concat(second.Select(row => row.OrderLineId))
            .Should().Equal(lineIds);
    }

    [Fact]
    public async Task GetPageAsync_FiltersByChannel()
    {
        string database = NewDatabaseName();
        await SeedAsync(database);

        using SalesDbContext context = CreateContext(database, TenantId);
        OrderLineRepublishRepository repository = new OrderLineRepublishRepository(context);

        IReadOnlyList<RepublishOrderLine> page = await repository.GetPageAsync(
            ChannelId, null, null, null, 100, CancellationToken.None);

        page.Should().HaveCount(2);
        page.Should().OnlyContain(row => row.ChannelId == ChannelId);
    }

    [Fact]
    public async Task GetPageAsync_FiltersByOrderDateRange()
    {
        string database = NewDatabaseName();
        await SeedAsync(database);

        using SalesDbContext context = CreateContext(database, TenantId);
        OrderLineRepublishRepository repository = new OrderLineRepublishRepository(context);

        IReadOnlyList<RepublishOrderLine> page = await repository.GetPageAsync(
            null, Day2, Day3, null, 100, CancellationToken.None);

        page.Should().HaveCount(1);
        page[0].OrderDate.Should().Be(Day2);
    }

    [Fact]
    public async Task GetPageAsync_DoesNotReadOtherTenants()
    {
        string database = NewDatabaseName();
        await SeedAsync(database);

        using SalesDbContext context = CreateContext(database, OtherTenantId);
        OrderLineRepublishRepository repository = new OrderLineRepublishRepository(context);

        IReadOnlyList<RepublishOrderLine> page = await repository.GetPageAsync(
            null, null, null, null, 100, CancellationToken.None);

        page.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPageAsync_ProjectsTheFactsTheEventNeeds()
    {
        string database = NewDatabaseName();
        Guid[] lineIds = await SeedAsync(database);

        using SalesDbContext context = CreateContext(database, TenantId);
        OrderLineRepublishRepository repository = new OrderLineRepublishRepository(context);

        IReadOnlyList<RepublishOrderLine> page = await repository.GetPageAsync(
            null, null, null, null, 100, CancellationToken.None);

        RepublishOrderLine row = page.Single(r => r.OrderLineId == lineIds[0]);

        row.ChannelId.Should().Be(ChannelId);
        row.ExternalOrderId.Should().Be("order-1");
        row.ExternalLineId.Should().Be("srid-1");
        row.ExternalProductId.Should().Be("sku-1");
        row.Currency.Should().Be("RUB");
        row.OrderDate.Should().Be(Day1);
    }

    [Fact]
    public void ParseCursor_RejectsMalformedToken()
    {
        Action act = () => OrderLineRepublishRepository.ParseCursor("not-a-guid");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ParseCursor_TreatsBlankTokenAsFirstPage()
    {
        OrderLineRepublishRepository.ParseCursor(null).Should().BeNull();
        OrderLineRepublishRepository.ParseCursor("  ").Should().BeNull();
    }

    private static async Task<Guid[]> SeedAsync(string database)
    {
        Guid firstLine = Guid.Parse("00000001-0000-0000-0000-000000000001");
        Guid secondLine = Guid.Parse("00000002-0000-0000-0000-000000000002");
        Guid thirdLine = Guid.Parse("00000003-0000-0000-0000-000000000003");

        using SalesDbContext context = CreateContext(database, TenantId);

        Order first = Order(Guid.Parse("00000000-0000-0000-0000-0000000000a1"), ChannelId, "order-1", Day1);
        Order second = Order(Guid.Parse("00000000-0000-0000-0000-0000000000a2"), OtherChannelId, "order-2", Day2);
        Order third = Order(Guid.Parse("00000000-0000-0000-0000-0000000000a3"), ChannelId, "order-3", Day3);

        context.Orders.AddRange(first, second, third);
        context.OrderLines.AddRange(
            Line(firstLine, first, "srid-1"),
            Line(secondLine, second, "srid-2"),
            Line(thirdLine, third, "srid-3"));

        await context.SaveChangesAsync();

        return new[] { firstLine, secondLine, thirdLine };
    }

    private static Order Order(Guid id, Guid channelId, string externalOrderId, DateTimeOffset orderDate)
    {
        return new Order
        {
            Id = id,
            ChannelId = channelId,
            ExternalOrderId = externalOrderId,
            OrderDate = orderDate,
            Currency = "RUB",
            ExternalStatus = "delivered",
            FirstImportedAt = DateTimeOffset.UnixEpoch,
            LastImportedAt = DateTimeOffset.UnixEpoch,
            CreatedAt = DateTimeOffset.UnixEpoch,
        };
    }

    private static OrderLine Line(Guid id, Order order, string externalLineId)
    {
        return new OrderLine
        {
            Id = id,
            OrderId = order.Id,
            LineKey = "line-" + externalLineId,
            ExternalLineId = externalLineId,
            ExternalProductId = "sku-1",
            Quantity = 1,
            Currency = "RUB",
            CreatedAt = DateTimeOffset.UnixEpoch,
        };
    }

    private static SalesDbContext CreateContext(string database, Guid tenantId)
    {
        DbContextOptions<SalesDbContext> options = new DbContextOptionsBuilder<SalesDbContext>()
            .UseInMemoryDatabase(database)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new SalesDbContext(options, CreateTenantContext(tenantId));
    }

    private static ITenantContext CreateTenantContext(Guid tenantId)
    {
        Mock<ITenantContext> tenant = new Mock<ITenantContext>();
        tenant.SetupGet(t => t.TenantId).Returns(tenantId);

        return tenant.Object;
    }

    private static string NewDatabaseName()
    {
        return "republish-" + Guid.NewGuid();
    }
}
