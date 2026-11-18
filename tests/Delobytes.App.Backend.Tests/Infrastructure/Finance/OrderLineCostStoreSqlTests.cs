using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Finance.Domain.Entities;
using Delobytes.App.Backend.Finance.Domain.Enums;
using Delobytes.App.Backend.Finance.Infrastructure.Persistence;
using Delobytes.App.Backend.Finance.Infrastructure.Persistence.Stores;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Infrastructure.Finance;

/// <summary>
/// Guards the write guard of <see cref="OrderLineCostStore"/>. "Newer wins" has to be decided by
/// the database: a client-side comparison would let two consumers that resolved the same line
/// overwrite each other, and an equal <c>ResolvedAt</c> must stay a no-op.
/// </summary>
public class OrderLineCostStoreSqlTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void BuildNewerThanStoredQuery_ComparesResolvedAtStrictlyInSql()
    {
        using FinanceDbContext context = CreateContext();
        OrderLineCostStore store = new OrderLineCostStore(context, CreateTenantContext());

        string sql = store.BuildNewerThanStoredQuery(Cost()).ToQueryString();

        sql.Should().Contain("FROM finance.\"OrderLineCosts\"");
        sql.Should().Contain("\"OrderLineId\" = @");
        sql.Should().Contain("\"ResolvedAt\" < @");
        sql.Should().NotContain("\"ResolvedAt\" <= @");
        sql.Should().Contain("\"TenantId\" = @ef_filter__TenantId");
        sql.Should().NotContain("::text");
    }

    [Fact]
    public void BuildNewerThanStoredQuery_DoesNotGuardOnRowVersion()
    {
        // xmin cannot order two results produced by different consumers, so it must not appear in the
        // predicate; a guard on it would make the write depend on update order instead of ResolvedAt.
        using FinanceDbContext context = CreateContext();
        OrderLineCostStore store = new OrderLineCostStore(context, CreateTenantContext());

        string sql = store.BuildNewerThanStoredQuery(Cost()).ToQueryString();
        string predicate = sql.Substring(sql.IndexOf("WHERE", StringComparison.Ordinal));

        predicate.Should().NotContain("xmin");
    }

    private static OrderLineCost Cost()
    {
        return new OrderLineCost
        {
            OrderLineId = Guid.NewGuid(),
            Status = OrderLineCostStatus.Resolved,
            ProductId = Guid.NewGuid(),
            AsOfDate = new DateOnly(2026, 3, 1),
            Quantity = 2,
            UnitTotalCost = 10m,
            TotalCost = 20m,
            IsComplete = true,
            ResolvedAt = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero),
            CreatedAt = DateTimeOffset.UnixEpoch,
            UpdatedAt = DateTimeOffset.UnixEpoch,
        };
    }

    private static FinanceDbContext CreateContext()
    {
        DbContextOptions<FinanceDbContext> options = new DbContextOptionsBuilder<FinanceDbContext>()
            .UseNpgsql("Host=localhost;Database=finance;Username=test;Password=test")
            .Options;

        return new FinanceDbContext(options, CreateTenantContext());
    }

    private static ITenantContext CreateTenantContext()
    {
        Mock<ITenantContext> tenant = new Mock<ITenantContext>();
        tenant.SetupGet(t => t.TenantId).Returns(TenantId);

        return tenant.Object;
    }
}
