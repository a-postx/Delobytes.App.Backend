using System.Security;
using System.Threading.Tasks;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.Infrastructure;

/// <summary>
/// Regression tests for the "no tenant in context" guard.
///
/// Root cause being tested: ITenantContext.TenantId is null whenever code runs outside
/// an HTTP request (a background job, a MassTransit consumer, a CLI tool). Before the
/// guard, SaveChanges() silently skipped stamping the shadow TenantId in that case,
/// leaving it at its CLR default (Guid.Empty) instead of a real tenant or a genuine
/// NULL. The row was then invisible to every tenant's HasQueryFilter forever — an
/// orphan record with no owner and no way to find it except IgnoreQueryFilters().
///
/// The guard makes SaveChanges() throw instead of persisting such a row.
/// </summary>
public class IntegrationsDbContextNoTenantGuardTests
{
    private static DbContextOptions<IntegrationsDbContext> BuildOptions()
    {
        return new DbContextOptionsBuilder<IntegrationsDbContext>()
            .UseInMemoryDatabase("no-tenant-guard-" + Guid.NewGuid())
            .Options;
    }

    private static Mock<ITenantContext> TenantMock(Guid? tenantId)
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
    /// Reproduces the ConnectionCreatedEventConsumer scenario: code adds a tenant-scoped
    /// entity while running with no resolved tenant (e.g. HttpContext is null inside a
    /// MassTransit consumer). SaveChanges must throw instead of writing Guid.Empty.
    /// </summary>
    [Fact]
    public async Task SaveChangesAsync_AddingTenantScopedEntity_WithNoTenant_Throws()
    {
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();
        Mock<ITenantContext> noTenantMock = TenantMock(null);

        SystemChannelTemplate template = BuildTemplate();

        using IntegrationsDbContext ctx = new IntegrationsDbContext(options, noTenantMock.Object);
        ctx.SystemChannelTemplates.Add(template); // not tenant-scoped: must not interfere
        ctx.Connections.Add(BuildActiveConnection(template.Id)); // tenant-scoped: must block the whole save

        Func<Task> act = () => ctx.SaveChangesAsync();

        await act.Should().ThrowAsync<SecurityException>()
            .WithMessage("*without a resolved TenantId*");
    }

    /// <summary>
    /// Confirms no partial write happens: after the exception, the connection must not
    /// exist in the store at all — not even as an orphan row with Guid.Empty.
    /// </summary>
    [Fact]
    public async Task SaveChangesAsync_AddingTenantScopedEntity_WithNoTenant_PersistsNothing()
    {
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();
        Mock<ITenantContext> noTenantMock = TenantMock(null);

        SystemChannelTemplate template = BuildTemplate();
        Connection orphanCandidate = BuildActiveConnection(template.Id);

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, noTenantMock.Object))
        {
            ctx.SystemChannelTemplates.Add(template);
            ctx.Connections.Add(orphanCandidate);

            await Assert.ThrowsAsync<SecurityException>(() => ctx.SaveChangesAsync());
        }

        // Fresh context, real tenant, bypassing filters entirely: the row must be absent.
        Mock<ITenantContext> tenantMock = TenantMock(Guid.NewGuid());
        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantMock.Object))
        {
            bool exists = await ctx.Connections
                .IgnoreQueryFilters()
                .AnyAsync(c => c.Id == orphanCandidate.Id);

            exists.Should().BeFalse("no orphan row with an unset TenantId should ever reach the store");
        }
    }

    /// <summary>
    /// Entities that do not implement ITenantScoped (e.g. SystemChannelTemplate) must
    /// keep saving fine even with no tenant in context — the guard is scoped strictly
    /// to ITenantScoped entities.
    /// </summary>
    [Fact]
    public async Task SaveChangesAsync_AddingNonTenantScopedEntity_WithNoTenant_Succeeds()
    {
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();
        Mock<ITenantContext> noTenantMock = TenantMock(null);

        SystemChannelTemplate template = BuildTemplate();

        using IntegrationsDbContext ctx = new IntegrationsDbContext(options, noTenantMock.Object);
        ctx.SystemChannelTemplates.Add(template);

        int saved = await ctx.SaveChangesAsync();

        saved.Should().Be(1);
    }

    /// <summary>
    /// Sanity check: the normal path (tenant present) is unaffected by the guard.
    /// </summary>
    [Fact]
    public async Task SaveChangesAsync_AddingTenantScopedEntity_WithTenant_StillWorks()
    {
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();
        Guid tenantId = Guid.NewGuid();
        Mock<ITenantContext> tenantMock = TenantMock(tenantId);

        SystemChannelTemplate template = BuildTemplate();
        Connection connection = BuildActiveConnection(template.Id);

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantMock.Object))
        {
            ctx.SystemChannelTemplates.Add(template);
            ctx.Connections.Add(connection);
            await ctx.SaveChangesAsync();
        }

        using (IntegrationsDbContext ctx = new IntegrationsDbContext(options, tenantMock.Object))
        {
            bool exists = await ctx.Connections.AnyAsync(c => c.Id == connection.Id);
            exists.Should().BeTrue();
        }
    }
}
