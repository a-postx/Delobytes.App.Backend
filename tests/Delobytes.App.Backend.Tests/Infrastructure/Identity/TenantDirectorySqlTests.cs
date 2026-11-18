using System;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Identity.Infrastructure.Persistence;
using Delobytes.App.Backend.Identity.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Infrastructure.Identity;

/// <summary>
/// Guards the keyset pagination of <see cref="TenantDirectory"/>: the ordering and the cursor
/// comparison must be translated into SQL. In-memory evaluation would order Guid values the .NET
/// way, which differs from PostgreSQL uuid ordering, and would silently skip or repeat tenants.
/// </summary>
public class TenantDirectorySqlTests
{
    [Fact]
    public void BuildPageQuery_WithoutCursor_OrdersAndLimitsInSql()
    {
        using IdentityDbContext context = CreateContext();

        string sql = TenantDirectory.BuildPageQuery(context, null, 200).ToQueryString();

        sql.Should().Contain("WHERE");
        sql.Should().Contain("ORDER BY");
        sql.Should().Contain("LIMIT");
        sql.Should().NotContain(" > @");
    }

    [Fact]
    public void BuildPageQuery_WithCursor_AddsKeysetPredicateInSql()
    {
        using IdentityDbContext context = CreateContext();
        Guid cursor = Guid.Parse("33333333-3333-3333-3333-333333333333");

        string sql = TenantDirectory.BuildPageQuery(context, cursor, 200).ToQueryString();

        sql.Should().Contain("ORDER BY");
        sql.Should().Contain("LIMIT");
        sql.Should().Contain("\"Id\" > @");
    }

    [Fact]
    public void BuildPageQuery_SqlHasNoInMemoryFallback()
    {
        using IdentityDbContext context = CreateContext();

        Action act = () => TenantDirectory.BuildPageQuery(context, Guid.NewGuid(), 200).ToQueryString();

        act.Should().NotThrow();
    }

    private static IdentityDbContext CreateContext()
    {
        DbContextOptions<IdentityDbContext> options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql("Host=localhost;Database=identity;Username=test;Password=test")
            .Options;

        Mock<ITenantContext> tenantContextMock = new Mock<ITenantContext>();
        tenantContextMock.SetupGet(c => c.TenantId).Returns((Guid?)null);

        return new IdentityDbContext(options, tenantContextMock.Object);
    }
}
