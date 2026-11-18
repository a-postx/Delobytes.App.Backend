using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Sales.Infrastructure.Persistence;
using Delobytes.App.Backend.Sales.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Infrastructure.Sales;

/// <summary>
/// Guards the translation of the re-publish page query. The join to the order, the narrowing and the
/// keyset must run in the database: a client-evaluated cursor would read the whole table, and the
/// old text ordering could not use the primary key index.
/// </summary>
public class OrderLineRepublishRepositorySqlTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Cursor = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public void BuildPageQuery_JoinsTheOrderAndProjectsInSql()
    {
        using SalesDbContext context = CreateContext();
        OrderLineRepublishRepository repository = new OrderLineRepublishRepository(context);

        string sql = repository.BuildPageQuery(null, null, null, null).ToQueryString();

        sql.Should().Contain("FROM sales.\"OrderLines\"");
        sql.Should().Contain("FROM sales.\"Orders\"");
        sql.Should().Contain("ORDER BY");
        sql.Should().NotContain("::text");
    }

    [Fact]
    public void BuildPageQuery_WithCursor_ComparesUuidWithoutTextCast()
    {
        using SalesDbContext context = CreateContext();
        OrderLineRepublishRepository repository = new OrderLineRepublishRepository(context);

        string sql = repository.BuildPageQuery(null, null, null, Cursor).ToQueryString();

        sql.Should().Contain("\"Id\" > @");
        sql.Should().NotContain("::text");
    }

    [Fact]
    public void BuildPageQuery_WithoutCursor_HasNoKeysetPredicate()
    {
        using SalesDbContext context = CreateContext();
        OrderLineRepublishRepository repository = new OrderLineRepublishRepository(context);

        string sql = repository.BuildPageQuery(null, null, null, null).ToQueryString();

        sql.Should().NotContain("\"Id\" > @");
    }

    [Fact]
    public void BuildPageQuery_TranslatesEveryFilterToSql()
    {
        using SalesDbContext context = CreateContext();
        OrderLineRepublishRepository repository = new OrderLineRepublishRepository(context);

        string sql = repository
            .BuildPageQuery(
                Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 8, 3, 0, 0, 0, TimeSpan.Zero),
                null)
            .ToQueryString();

        sql.Should().Contain("\"ChannelId\" = @");
        sql.Should().Contain("\"OrderDate\" >= @");
        sql.Should().Contain("\"OrderDate\" < @");
        sql.Should().NotContain("\"OrderDate\" <= @");
    }

    private static SalesDbContext CreateContext()
    {
        DbContextOptions<SalesDbContext> options = new DbContextOptionsBuilder<SalesDbContext>()
            .UseNpgsql("Host=localhost;Database=sales;Username=test;Password=test")
            .Options;

        Mock<ITenantContext> tenant = new Mock<ITenantContext>();
        tenant.SetupGet(t => t.TenantId).Returns(TenantId);

        return new SalesDbContext(options, tenant.Object);
    }
}
