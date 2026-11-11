using System;
using System.Linq;
using System.Threading.Tasks;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Persistence.Interceptors;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Infrastructure.Persistence;

public class AuditableEntityInterceptorTests
{
    private static readonly DateTimeOffset OldTimestamp = new DateTimeOffset(2020, 6, 1, 8, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset FixedNow = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);

    private sealed class FixedClock : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedClock(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow()
        {
            return _utcNow;
        }
    }

    private static Mock<IUserContext> UserMock(Guid? userId)
    {
        Mock<IUserContext> mock = new Mock<IUserContext>();
        mock.Setup(u => u.UserId).Returns(userId);
        return mock;
    }

    private static AuditableEntityInterceptor BuildInterceptor(Guid? userId, DateTimeOffset now)
    {
        return new AuditableEntityInterceptor(UserMock(userId).Object, new FixedClock(now));
    }

    private static AuditableWidget BuildWidget(string name)
    {
        return new AuditableWidget
        {
            Id = Guid.NewGuid(),
            Name = name,
            CreatedAt = OldTimestamp,
        };
    }

    [Fact]
    public void SavingChanges_WhenEntityAdded_StampsCreationAuditFields()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();
        AuditableWidget widget = BuildWidget("created");
        string databaseName = PersistenceTestOptions.NewDatabaseName("audit-add");

        // Act
        using (TestPersistenceDbContext context = new TestPersistenceDbContext(
            PersistenceTestOptions.Build<TestPersistenceDbContext>(databaseName, BuildInterceptor(userId, FixedNow)),
            TenantContextMocks.WithTenant(tenantId).Object))
        {
            context.AuditableWidgets.Add(widget);
            context.SaveChanges();
        }

        // Assert
        widget.CreatedAt.Should().Be(FixedNow);
        widget.UpdatedAt.Should().BeNull();
        widget.CreatedByUserId.Should().Be(userId);
        widget.UpdatedByUserId.Should().BeNull();

        AuditableWidget stored = Reload(databaseName, tenantId, widget.Id);
        stored.CreatedAt.Should().Be(FixedNow);
        stored.CreatedByUserId.Should().Be(userId);
        stored.UpdatedAt.Should().BeNull();
        stored.UpdatedByUserId.Should().BeNull();
    }

    [Fact]
    public async Task SavingChangesAsync_WhenEntityModified_StampsUpdateFieldsAndPreservesCreationFields()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        Guid creatorId = Guid.NewGuid();
        Guid editorId = Guid.NewGuid();
        string databaseName = PersistenceTestOptions.NewDatabaseName("audit-modify");

        AuditableWidget seeded = BuildWidget("seeded");
        seeded.CreatedByUserId = creatorId;

        using (TestPersistenceDbContext seed = new TestPersistenceDbContext(
            PersistenceTestOptions.Build<TestPersistenceDbContext>(databaseName),
            TenantContextMocks.WithTenant(tenantId).Object))
        {
            seed.AuditableWidgets.Add(seeded);
            await seed.SaveChangesAsync();
        }

        // Act
        using (TestPersistenceDbContext context = new TestPersistenceDbContext(
            PersistenceTestOptions.Build<TestPersistenceDbContext>(databaseName, BuildInterceptor(editorId, FixedNow)),
            TenantContextMocks.WithTenant(tenantId).Object))
        {
            AuditableWidget widget = await context.AuditableWidgets.SingleAsync(w => w.Id == seeded.Id);
            widget.Name = "modified";

            // Attempt to rewrite the immutable creation fields on a modified entity.
            widget.CreatedAt = FixedNow.AddDays(1);
            widget.CreatedByUserId = editorId;

            await context.SaveChangesAsync();

            widget.UpdatedAt.Should().Be(FixedNow);
            widget.UpdatedByUserId.Should().Be(editorId);
        }

        // Assert
        AuditableWidget stored = Reload(databaseName, tenantId, seeded.Id);
        stored.Name.Should().Be("modified");
        stored.UpdatedAt.Should().Be(FixedNow);
        stored.UpdatedByUserId.Should().Be(editorId);
        stored.CreatedAt.Should().Be(OldTimestamp);
        stored.CreatedByUserId.Should().Be(creatorId);
    }

    [Fact]
    public void SavingChanges_WhenUserContextHasNoUser_LeavesUserIdsNull()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        AuditableWidget widget = BuildWidget("anonymous");
        string databaseName = PersistenceTestOptions.NewDatabaseName("audit-no-user");

        // Act
        using TestPersistenceDbContext context = new TestPersistenceDbContext(
            PersistenceTestOptions.Build<TestPersistenceDbContext>(databaseName, BuildInterceptor(null, FixedNow)),
            TenantContextMocks.WithTenant(tenantId).Object);
        context.AuditableWidgets.Add(widget);
        context.SaveChanges();

        // Assert
        widget.CreatedAt.Should().Be(FixedNow);
        widget.CreatedByUserId.Should().BeNull();
        widget.UpdatedByUserId.Should().BeNull();
    }

    [Fact]
    public void SavingChanges_WhenEntityIsNotAuditable_DoesNotChangeIt()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        Widget widget = new Widget
        {
            Id = Guid.NewGuid(),
            Name = "plain widget",
            CreatedAt = OldTimestamp,
        };

        // Act
        using TestPersistenceDbContext context = new TestPersistenceDbContext(
            PersistenceTestOptions.Build<TestPersistenceDbContext>(
                PersistenceTestOptions.NewDatabaseName("audit-plain"),
                BuildInterceptor(Guid.NewGuid(), FixedNow)),
            TenantContextMocks.WithTenant(tenantId).Object);
        context.Widgets.Add(widget);
        context.SaveChanges();

        // Assert
        widget.CreatedAt.Should().Be(OldTimestamp);
        context.Widgets.Single().Name.Should().Be("plain widget");
    }

    private static AuditableWidget Reload(string databaseName, Guid tenantId, Guid widgetId)
    {
        using TestPersistenceDbContext context = new TestPersistenceDbContext(
            PersistenceTestOptions.Build<TestPersistenceDbContext>(databaseName),
            TenantContextMocks.WithTenant(tenantId).Object);

        return context.AuditableWidgets.IgnoreQueryFilters().Single(w => w.Id == widgetId);
    }
}
