using System.Collections.Generic;
using System.Threading.Tasks;
using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.Infrastructure;

/// <summary>
/// Verifies that IntegrationsDbContext query filters enforce strict tenant isolation.
///
/// Root cause being tested: OnModelCreating is called once per unique DbContextOptions
/// instance (the model is cached singleton-style). If the query filter captures the
/// ITenantContext reference from the *first* DbContext that triggers model building
/// (Expression.Constant(_tenantContext)), every subsequent DbContext in a different
/// DI scope uses that frozen reference — leaking Tenant A's data to Tenant B.
///
/// The fix (Expression.Field referencing `this`) makes EF Core evaluate _tenantContext
/// from the *current* DbContext instance at query execution time.
/// </summary>
public class IntegrationsDbContextTenantIsolationTests
{
    // Unique DB name per test prevents cross-test state leakage.
    private static DbContextOptions<IntegrationsDbContext> BuildOptions()
    {
        return new DbContextOptionsBuilder<IntegrationsDbContext>()
            .UseInMemoryDatabase("isolation-" + Guid.NewGuid())
            .Options;
    }

    private static Mock<ITenantContext> TenantMock(Guid tenantId)
    {
        Mock<ITenantContext> mock = new Mock<ITenantContext>();
        mock.Setup(t => t.TenantId).Returns(tenantId);
        return mock;
    }

    private static SystemChannelTemplate BuildTemplate()
    {
        return new SystemChannelTemplate
        {
            Id = Guid.NewGuid(),
            Code = "wildberries",
            DisplayName = "Wildberries",
            ApiBaseUrl = "https://suppliers-api.wildberries.ru",
            ApiVersion = "v3",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    private static Connection BuildActiveConnection(Guid channelId)
    {
        return new Connection
        {
            Id = Guid.NewGuid(),
            ChannelId = channelId,
            Name = "Test connection",
            ApiKey = "test-key",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>
    /// Core regression test for the tenant isolation bug.
    ///
    /// Scenario: TenantA's context is created first (triggers model build). TenantB
    /// then creates its own context using the same DbContextOptions (as DI does with
    /// AddDbContext). If the query filter was frozen to TenantA's ITenantContext,
    /// TenantB would see TenantA's connections instead of its own.
    /// </summary>
    [Fact]
    public async Task QueryFilter_SecondContextWithDifferentTenant_DoesNotLeakFirstTenantData()
    {
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();

        Guid tenantAId = Guid.NewGuid();
        Guid tenantBId = Guid.NewGuid();

        Mock<ITenantContext> tenantAMock = TenantMock(tenantAId);
        Mock<ITenantContext> tenantBMock = TenantMock(tenantBId);

        SystemChannelTemplate template = BuildTemplate();

        // TenantA context — triggers OnModelCreating, seeds one connection.
        using (IntegrationsDbContext ctxA = new IntegrationsDbContext(options, tenantAMock.Object))
        {
            ctxA.SystemChannelTemplates.Add(template);
            ctxA.Connections.Add(BuildActiveConnection(template.Id));
            await ctxA.SaveChangesAsync();
        }

        // TenantB context — reuses cached model, seeds its own connection.
        using (IntegrationsDbContext ctxB = new IntegrationsDbContext(options, tenantBMock.Object))
        {
            ctxB.Connections.Add(BuildActiveConnection(template.Id));
            await ctxB.SaveChangesAsync();
        }

        // TenantB must see exactly one connection — its own, not TenantA's.
        using (IntegrationsDbContext ctxB = new IntegrationsDbContext(options, tenantBMock.Object))
        {
            List<Connection> results = await ctxB.Connections.ToListAsync();

            results.Should().HaveCount(1, "TenantB must not see TenantA's connections");
        }
    }

    /// <summary>
    /// Inverse: after TenantB is created, TenantA must still see only its own data.
    /// </summary>
    [Fact]
    public async Task QueryFilter_FirstTenantContext_StillIsolatedAfterSecondTenantIsCreated()
    {
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();

        Guid tenantAId = Guid.NewGuid();
        Guid tenantBId = Guid.NewGuid();

        Mock<ITenantContext> tenantAMock = TenantMock(tenantAId);
        Mock<ITenantContext> tenantBMock = TenantMock(tenantBId);

        SystemChannelTemplate template = BuildTemplate();

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantAMock.Object))
        {
            ctx.SystemChannelTemplates.Add(template);
            ctx.Connections.Add(BuildActiveConnection(template.Id));
            await ctx.SaveChangesAsync();
        }

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantBMock.Object))
        {
            ctx.Connections.Add(BuildActiveConnection(template.Id));
            await ctx.SaveChangesAsync();
        }

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantAMock.Object))
        {
            List<Connection> results = await ctx.Connections.ToListAsync();

            results.Should().HaveCount(1, "TenantA must not see TenantB's connections");
        }
    }

    /// <summary>
    /// Looking up a connection by its known ID must return null when the querying
    /// tenant does not own that connection. This is the DeleteConnection attack vector:
    /// a user from TenantB passes a connectionId from TenantA.
    /// </summary>
    [Fact]
    public async Task QueryFilter_LookupByKnownId_ReturnsNullForCrossTenantAccess()
    {
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();

        Guid tenantAId = Guid.NewGuid();
        Guid tenantBId = Guid.NewGuid();

        Mock<ITenantContext> tenantAMock = TenantMock(tenantAId);
        Mock<ITenantContext> tenantBMock = TenantMock(tenantBId);

        SystemChannelTemplate template = BuildTemplate();
        Connection tenantAConnection = BuildActiveConnection(template.Id);

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantAMock.Object))
        {
            ctx.SystemChannelTemplates.Add(template);
            ctx.Connections.Add(tenantAConnection);
            await ctx.SaveChangesAsync();
        }

        // TenantB knows TenantA's connection ID (e.g., from a previous leak or brute force).
        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantBMock.Object))
        {
            Connection? result = await ctx.Connections
                .FirstOrDefaultAsync(c => c.Id == tenantAConnection.Id);

            result.Should().BeNull("cross-tenant ID lookup must return null");
        }
    }

    /// <summary>
    /// Verifies that SaveChanges stamps the TenantId shadow property from the current
    /// context's ITenantContext, not from the model-cached one. If this breaks, seeded
    /// data would have wrong TenantIds and all isolation tests would be meaningless.
    /// </summary>
    [Fact]
    public async Task SaveChanges_StampsTenantIdShadowProperty_FromCurrentContextTenant()
    {
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();

        Guid tenantAId = Guid.NewGuid();
        Guid tenantBId = Guid.NewGuid();

        Mock<ITenantContext> tenantAMock = TenantMock(tenantAId);
        Mock<ITenantContext> tenantBMock = TenantMock(tenantBId);

        SystemChannelTemplate template = BuildTemplate();
        Connection connA = BuildActiveConnection(template.Id);
        Connection connB = BuildActiveConnection(template.Id);

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantAMock.Object))
        {
            ctx.SystemChannelTemplates.Add(template);
            ctx.Connections.Add(connA);
            await ctx.SaveChangesAsync();
        }

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantBMock.Object))
        {
            ctx.Connections.Add(connB);
            await ctx.SaveChangesAsync();
        }

        // IgnoreQueryFilters to read raw shadow property values regardless of current tenant.
        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantAMock.Object))
        {
            Guid? stampedOnA = await ctx.Connections
                .IgnoreQueryFilters()
                .Where(c => c.Id == connA.Id)
                .Select(c => EF.Property<Guid?>(c, "TenantId"))
                .SingleAsync();

            Guid? stampedOnB = await ctx.Connections
                .IgnoreQueryFilters()
                .Where(c => c.Id == connB.Id)
                .Select(c => EF.Property<Guid?>(c, "TenantId"))
                .SingleAsync();

            stampedOnA.Should().Be(tenantAId, "connA must be stamped with TenantA's ID");
            stampedOnB.Should().Be(tenantBId, "connB must be stamped with TenantB's ID");
        }
    }
}
