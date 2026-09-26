using System.Security;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Infrastructure.Persistence;
using Delobytes.App.Backend.Contracts.Interfaces;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Catalog;

/// <summary>
/// Regression tests for the "no tenant in context" guard on CatalogDbContext.
///
/// Reproduces the real bug found in ConnectionCreatedEventConsumer: MassTransit consumers
/// run in their own DI scope, where ITenantContext resolves from IHttpContextAccessor and
/// HttpContext is null, so TenantId is always null. Before the guard, adding a Channel from
/// such a consumer silently wrote TenantId = Guid.Empty and the row became permanently
/// invisible to every real tenant. The guard turns that into an immediate exception.
/// </summary>
public class CatalogDbContextNoTenantGuardTests
{
    private static DbContextOptions<CatalogDbContext> BuildOptions()
    {
        return new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase("catalog-no-tenant-guard-" + Guid.NewGuid())
            .Options;
    }

    private static Mock<ITenantContext> TenantMock(Guid? tenantId)
    {
        Mock<ITenantContext> mock = new Mock<ITenantContext>();
        mock.Setup(t => t.TenantId).Returns(tenantId);
        return mock;
    }

    private static Channel BuildChannel()
    {
        return new Channel
        {
            Id = Guid.NewGuid(),
            Name = "Wildberries",
            IsCustom = false,
            IsActive = true
        };
    }

    [Fact]
    public async Task SaveChangesAsync_ConsumerWithoutHttpContext_AddingChannel_Throws()
    {
        DbContextOptions<CatalogDbContext> options = BuildOptions();
        Mock<ITenantContext> noTenantMock = TenantMock(null); // e.g. IHttpContextAccessor.HttpContext == null

        using CatalogDbContext ctx = new CatalogDbContext(options, noTenantMock.Object);
        ctx.Channels.Add(BuildChannel());

        Func<Task> act = () => ctx.SaveChangesAsync();

        await act.Should().ThrowAsync<SecurityException>()
            .WithMessage("*Channel*without a resolved TenantId*");
    }

    [Fact]
    public async Task SaveChangesAsync_ConsumerWithoutHttpContext_AddingChannel_PersistsNothing()
    {
        DbContextOptions<CatalogDbContext> options = BuildOptions();
        Mock<ITenantContext> noTenantMock = TenantMock(null);
        Channel orphanCandidate = BuildChannel();

        using (CatalogDbContext ctx = new CatalogDbContext(options, noTenantMock.Object))
        {
            ctx.Channels.Add(orphanCandidate);
            await Assert.ThrowsAsync<SecurityException>(() => ctx.SaveChangesAsync());
        }

        Mock<ITenantContext> tenantMock = TenantMock(Guid.NewGuid());
        using (CatalogDbContext ctx = new CatalogDbContext(options, tenantMock.Object))
        {
            bool exists = await ctx.Channels
                .IgnoreQueryFilters()
                .AnyAsync(c => c.Id == orphanCandidate.Id);

            exists.Should().BeFalse("no Channel with an unset TenantId should ever reach the store");
        }
    }

    [Fact]
    public async Task SaveChangesAsync_WithTenant_AddingChannel_StillWorks()
    {
        DbContextOptions<CatalogDbContext> options = BuildOptions();
        Guid tenantId = Guid.NewGuid();
        Mock<ITenantContext> tenantMock = TenantMock(tenantId);
        Channel channel = BuildChannel();

        using (CatalogDbContext ctx = new CatalogDbContext(options, tenantMock.Object))
        {
            ctx.Channels.Add(channel);
            await ctx.SaveChangesAsync();
        }

        using (CatalogDbContext ctx = new CatalogDbContext(options, tenantMock.Object))
        {
            bool exists = await ctx.Channels.AnyAsync(c => c.Id == channel.Id);
            exists.Should().BeTrue();
        }
    }
}
