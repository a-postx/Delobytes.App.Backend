using System;
using System.Linq;
using System.Security;
using System.Threading.Tasks;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Infrastructure.Persistence;

public class TenantScopedDbContextTests
{
    private static Widget BuildWidget(string name)
    {
        return new Widget
        {
            Id = Guid.NewGuid(),
            Name = name,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    private static Badge BuildBadge(Guid tenantId, string label)
    {
        return new Badge
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Label = label,
        };
    }

    [Fact]
    public void SaveChanges_WhenEntityAdded_StampsTenantId()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        Mock<ITenantContext> tenant = TenantContextMocks.WithTenant(tenantId);
        DbContextOptions<TestPersistenceDbContext> options = PersistenceTestOptions.Build<TestPersistenceDbContext>(
            PersistenceTestOptions.NewDatabaseName("stamp"));
        Widget widget = BuildWidget("stamped");

        // Act
        using (TestPersistenceDbContext context = new TestPersistenceDbContext(options, tenant.Object))
        {
            EntityEntry<Widget> entry = context.Widgets.Add(widget);
            context.SaveChanges();

            // Assert
            entry.Property<Guid>("TenantId").CurrentValue.Should().Be(tenantId);
        }

        using (TestPersistenceDbContext verify = new TestPersistenceDbContext(options, tenant.Object))
        {
            Widget stored = verify.Widgets.Single();
            verify.Entry(stored).Property<Guid>("TenantId").CurrentValue.Should().Be(tenantId);
        }
    }

    [Fact]
    public async Task SaveChangesAsync_WhenEntityAddedWithForeignTenant_OverwritesWithCurrentTenant()
    {
        // Arrange
        Guid currentTenantId = Guid.NewGuid();
        Guid foreignTenantId = Guid.NewGuid();
        Mock<ITenantContext> tenant = TenantContextMocks.WithTenant(currentTenantId);
        DbContextOptions<TestPersistenceDbContext> options = PersistenceTestOptions.Build<TestPersistenceDbContext>(
            PersistenceTestOptions.NewDatabaseName("overwrite"));
        Widget widget = BuildWidget("overwritten");

        // Act
        using TestPersistenceDbContext context = new TestPersistenceDbContext(options, tenant.Object);
        EntityEntry<Widget> entry = context.Widgets.Add(widget);
        entry.Property<Guid>("TenantId").CurrentValue = foreignTenantId;

        await context.SaveChangesAsync();

        // Assert
        entry.Property<Guid>("TenantId").CurrentValue.Should().Be(currentTenantId);
    }

    [Fact]
    public async Task SaveChangesAsync_WhenNoTenantAndEntityAdded_ThrowsSecurityExceptionAndPersistsNothing()
    {
        // Arrange
        Mock<ITenantContext> noTenant = TenantContextMocks.WithTenant(null);
        DbContextOptions<TestPersistenceDbContext> options = PersistenceTestOptions.Build<TestPersistenceDbContext>(
            PersistenceTestOptions.NewDatabaseName("no-tenant-add"));

        using (TestPersistenceDbContext context = new TestPersistenceDbContext(options, noTenant.Object))
        {
            context.Widgets.Add(BuildWidget("orphan"));

            // Act
            Func<Task> act = () => context.SaveChangesAsync();

            // Assert
            await act.Should().ThrowAsync<SecurityException>()
                .WithMessage("*Widget*without a resolved TenantId*");
        }

        Mock<ITenantContext> tenant = TenantContextMocks.WithTenant(Guid.NewGuid());
        using TestPersistenceDbContext verify = new TestPersistenceDbContext(options, tenant.Object);
        (await verify.Widgets.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task SaveChangesAsync_WhenNoTenantAndSeveralEntityTypesAdded_ThrowsWithAllEntityNames()
    {
        // Arrange
        Mock<ITenantContext> noTenant = TenantContextMocks.WithTenant(null);
        DbContextOptions<TestPersistenceDbContext> options = PersistenceTestOptions.Build<TestPersistenceDbContext>(
            PersistenceTestOptions.NewDatabaseName("no-tenant-many"));

        using TestPersistenceDbContext context = new TestPersistenceDbContext(options, noTenant.Object);
        context.Widgets.Add(BuildWidget("first"));
        context.Badges.Add(BuildBadge(Guid.NewGuid(), "second"));

        // Act
        Func<Task> act = () => context.SaveChangesAsync();

        // Assert
        await act.Should().ThrowAsync<SecurityException>()
            .Where(e => e.Message.Contains("entities (")
                && e.Message.Contains(nameof(Widget))
                && e.Message.Contains(nameof(Badge))
                && e.Message.Contains("without a resolved TenantId"));
    }

    [Fact]
    public async Task SaveChangesAsync_WhenNoTenantAndEntityModified_ThrowsSecurityException()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        DbContextOptions<TestPersistenceDbContext> options = PersistenceTestOptions.Build<TestPersistenceDbContext>(
            PersistenceTestOptions.NewDatabaseName("no-tenant-modify"));

        using (TestPersistenceDbContext seed = new TestPersistenceDbContext(
            options,
            TenantContextMocks.WithTenant(tenantId).Object))
        {
            seed.Widgets.Add(BuildWidget("seeded"));
            await seed.SaveChangesAsync();
        }

        using TestPersistenceDbContext context = new TestPersistenceDbContext(
            options,
            TenantContextMocks.WithTenant(null).Object);
        Widget widget = await context.Widgets.IgnoreQueryFilters().SingleAsync();
        widget.Name = "modified without tenant";

        // Act
        Func<Task> act = () => context.SaveChangesAsync();

        // Assert
        await act.Should().ThrowAsync<SecurityException>()
            .WithMessage("*Widget*without a resolved TenantId*");
    }

    [Fact]
    public async Task SaveChangesAsync_WhenNoTenantAndEntityDeleted_PersistsDeletion()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        DbContextOptions<TestPersistenceDbContext> options = PersistenceTestOptions.Build<TestPersistenceDbContext>(
            PersistenceTestOptions.NewDatabaseName("no-tenant-delete"));

        using (TestPersistenceDbContext seed = new TestPersistenceDbContext(
            options,
            TenantContextMocks.WithTenant(tenantId).Object))
        {
            seed.Widgets.Add(BuildWidget("to delete"));
            await seed.SaveChangesAsync();
        }

        using (TestPersistenceDbContext context = new TestPersistenceDbContext(
            options,
            TenantContextMocks.WithTenant(null).Object))
        {
            Widget widget = await context.Widgets.IgnoreQueryFilters().SingleAsync();
            context.Widgets.Remove(widget);

            // Act
            Func<Task> act = () => context.SaveChangesAsync();

            // Assert
            await act.Should().NotThrowAsync();
        }

        using TestPersistenceDbContext verify = new TestPersistenceDbContext(
            options,
            TenantContextMocks.WithTenant(tenantId).Object);
        (await verify.Widgets.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task SaveChangesAsync_WhenModifiedEntityHasEmptyTenantId_ReparentsToAddedAndStampsTenant()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        Mock<ITenantContext> tenant = TenantContextMocks.WithTenant(tenantId);
        DbContextOptions<TestPersistenceDbContext> options = PersistenceTestOptions.Build<TestPersistenceDbContext>(
            PersistenceTestOptions.NewDatabaseName("reparent"));
        Widget widget = BuildWidget("tracked as modified");

        // Act
        using (TestPersistenceDbContext context = new TestPersistenceDbContext(options, tenant.Object))
        {
            EntityEntry<Widget> entry = context.Attach(widget);
            entry.State = EntityState.Modified;
            entry.Property<Guid>("TenantId").CurrentValue = Guid.Empty;

            await context.SaveChangesAsync();
        }

        // Assert
        using TestPersistenceDbContext verify = new TestPersistenceDbContext(options, tenant.Object);
        Widget stored = await verify.Widgets.SingleAsync();
        stored.Name.Should().Be("tracked as modified");
        verify.Entry(stored).Property<Guid>("TenantId").CurrentValue.Should().Be(tenantId);
    }

    [Fact]
    public async Task SaveChangesAsync_WhenModifyingForeignTenantEntity_ThrowsSecurityException()
    {
        // Arrange
        Guid ownerTenantId = Guid.NewGuid();
        Guid otherTenantId = Guid.NewGuid();
        DbContextOptions<TestPersistenceDbContext> options = PersistenceTestOptions.Build<TestPersistenceDbContext>(
            PersistenceTestOptions.NewDatabaseName("cross-tenant-modify"));

        using (TestPersistenceDbContext seed = new TestPersistenceDbContext(
            options,
            TenantContextMocks.WithTenant(ownerTenantId).Object))
        {
            seed.Widgets.Add(BuildWidget("owned"));
            await seed.SaveChangesAsync();
        }

        using TestPersistenceDbContext context = new TestPersistenceDbContext(
            options,
            TenantContextMocks.WithTenant(otherTenantId).Object);
        Widget widget = await context.Widgets.IgnoreQueryFilters().SingleAsync();
        widget.Name = "tampered";

        // Act
        Func<Task> act = () => context.SaveChangesAsync();

        // Assert
        await act.Should().ThrowAsync<SecurityException>()
            .WithMessage($"Cross-tenant write detected on '{nameof(Widget)}':*");
    }

    [Fact]
    public async Task SaveChangesAsync_WhenDeletingForeignTenantEntity_ThrowsSecurityException()
    {
        // Arrange
        Guid ownerTenantId = Guid.NewGuid();
        Guid otherTenantId = Guid.NewGuid();
        DbContextOptions<TestPersistenceDbContext> options = PersistenceTestOptions.Build<TestPersistenceDbContext>(
            PersistenceTestOptions.NewDatabaseName("cross-tenant-delete"));

        using (TestPersistenceDbContext seed = new TestPersistenceDbContext(
            options,
            TenantContextMocks.WithTenant(ownerTenantId).Object))
        {
            seed.Widgets.Add(BuildWidget("owned"));
            await seed.SaveChangesAsync();
        }

        using TestPersistenceDbContext context = new TestPersistenceDbContext(
            options,
            TenantContextMocks.WithTenant(otherTenantId).Object);
        Widget widget = await context.Widgets.IgnoreQueryFilters().SingleAsync();
        context.Widgets.Remove(widget);

        // Act
        Func<Task> act = () => context.SaveChangesAsync();

        // Assert
        await act.Should().ThrowAsync<SecurityException>()
            .WithMessage($"Cross-tenant write detected on '{nameof(Widget)}':*");
    }

    [Fact]
    public async Task SaveChangesAsync_WhenOnlyNonScopedEntityChanged_DoesNotRequireTenant()
    {
        // Arrange
        Mock<ITenantContext> noTenant = TenantContextMocks.WithTenant(null);
        DbContextOptions<TestPersistenceDbContext> options = PersistenceTestOptions.Build<TestPersistenceDbContext>(
            PersistenceTestOptions.NewDatabaseName("plain"));

        using TestPersistenceDbContext context = new TestPersistenceDbContext(options, noTenant.Object);
        context.Notes.Add(new PlainNote { Id = Guid.NewGuid(), Text = "note" });

        // Act
        Func<Task> act = () => context.SaveChangesAsync();

        // Assert
        await act.Should().NotThrowAsync();
        (await context.Notes.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task QueryFilter_ReturnsOnlyCurrentTenantRows()
    {
        // Arrange
        Guid tenantAId = Guid.NewGuid();
        Guid tenantBId = Guid.NewGuid();
        DbContextOptions<TestPersistenceDbContext> options = PersistenceTestOptions.Build<TestPersistenceDbContext>(
            PersistenceTestOptions.NewDatabaseName("filter"));

        using (TestPersistenceDbContext contextA = new TestPersistenceDbContext(
            options,
            TenantContextMocks.WithTenant(tenantAId).Object))
        {
            contextA.Widgets.Add(BuildWidget("for A"));
            await contextA.SaveChangesAsync();
        }

        using (TestPersistenceDbContext contextB = new TestPersistenceDbContext(
            options,
            TenantContextMocks.WithTenant(tenantBId).Object))
        {
            contextB.Widgets.Add(BuildWidget("for B"));
            await contextB.SaveChangesAsync();
        }

        // Act
        using TestPersistenceDbContext tenantAContext = new TestPersistenceDbContext(
            options,
            TenantContextMocks.WithTenant(tenantAId).Object);

        // Assert
        Widget visible = await tenantAContext.Widgets.SingleAsync();
        visible.Name.Should().Be("for A");
        tenantAContext.Entry(visible).Property<Guid>("TenantId").CurrentValue.Should().Be(tenantAId);
        (await tenantAContext.Widgets.IgnoreQueryFilters().CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task QueryFilter_ForEntityWithRealTenantId_FiltersByTenantId()
    {
        // Arrange
        Guid tenantAId = Guid.NewGuid();
        Guid tenantBId = Guid.NewGuid();
        DbContextOptions<TestPersistenceDbContext> options = PersistenceTestOptions.Build<TestPersistenceDbContext>(
            PersistenceTestOptions.NewDatabaseName("filter-real"));

        using (TestPersistenceDbContext contextA = new TestPersistenceDbContext(
            options,
            TenantContextMocks.WithTenant(tenantAId).Object))
        {
            contextA.Badges.Add(BuildBadge(Guid.Empty, "badge A"));
            await contextA.SaveChangesAsync();
        }

        using (TestPersistenceDbContext contextB = new TestPersistenceDbContext(
            options,
            TenantContextMocks.WithTenant(tenantBId).Object))
        {
            contextB.Badges.Add(BuildBadge(Guid.Empty, "badge B"));
            await contextB.SaveChangesAsync();
        }

        // Act
        using TestPersistenceDbContext tenantAContext = new TestPersistenceDbContext(
            options,
            TenantContextMocks.WithTenant(tenantAId).Object);

        // Assert
        Badge visible = await tenantAContext.Badges.SingleAsync();
        visible.Label.Should().Be("badge A");
        visible.TenantId.Should().Be(tenantAId);
        (await tenantAContext.Badges.IgnoreQueryFilters().CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task QueryFilter_DoesNotCaptureTenantFromModelBuild()
    {
        // Arrange
        Guid firstTenantId = Guid.NewGuid();
        Guid secondTenantId = Guid.NewGuid();
        Mock<ITenantContext> tenant = TenantContextMocks.WithTenant(firstTenantId);
        DbContextOptions<TestPersistenceDbContext> options = PersistenceTestOptions.Build<TestPersistenceDbContext>(
            PersistenceTestOptions.NewDatabaseName("live-tenant"));

        using (TestPersistenceDbContext context = new TestPersistenceDbContext(options, tenant.Object))
        {
            context.Widgets.Add(BuildWidget("first tenant"));
            await context.SaveChangesAsync();
        }

        // Act
        tenant.Setup(t => t.TenantId).Returns(secondTenantId);

        using TestPersistenceDbContext secondContext = new TestPersistenceDbContext(options, tenant.Object);
        int visibleToSecondTenant = await secondContext.Widgets.CountAsync();
        int totalRows = await secondContext.Widgets.IgnoreQueryFilters().CountAsync();

        // Assert
        visibleToSecondTenant.Should().Be(0);
        totalRows.Should().Be(1);
    }

    [Fact]
    public async Task SaveChangesAsync_WhenEntityExemptAndNoTenant_PersistsOwnTenantId()
    {
        // Arrange
        Guid ownTenantId = Guid.NewGuid();
        Mock<ITenantContext> noTenant = TenantContextMocks.WithTenant(null);
        DbContextOptions<ExemptPersistenceDbContext> options = PersistenceTestOptions.Build<ExemptPersistenceDbContext>(
            PersistenceTestOptions.NewDatabaseName("exempt"));

        using (ExemptPersistenceDbContext context = new ExemptPersistenceDbContext(options, noTenant.Object))
        {
            context.Badges.Add(BuildBadge(ownTenantId, "own tenant"));

            // Act
            Func<Task> act = () => context.SaveChangesAsync();

            // Assert
            await act.Should().NotThrowAsync();
        }

        using ExemptPersistenceDbContext verify = new ExemptPersistenceDbContext(
            options,
            TenantContextMocks.WithTenant(ownTenantId).Object);
        Badge stored = await verify.Badges.SingleAsync();
        stored.TenantId.Should().Be(ownTenantId);
    }
}
