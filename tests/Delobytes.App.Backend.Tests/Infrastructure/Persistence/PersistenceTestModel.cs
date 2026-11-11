using System;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;

namespace Delobytes.App.Backend.Tests.Infrastructure.Persistence;

internal sealed class Widget : ITenantScoped
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }
}

internal sealed class Badge : ITenantScoped
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public string Label { get; set; } = string.Empty;
}

internal sealed class VersionedWidget : IRowVersionedEntity, ITenantScoped
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public uint RowVersion { get; set; }
}

internal sealed class AuditableWidget : IAuditableEntity, ITenantScoped
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? CreatedByUserId { get; set; }

    public Guid? UpdatedByUserId { get; set; }
}

internal sealed class PlainNote
{
    public Guid Id { get; set; }

    public string Text { get; set; } = string.Empty;
}

internal class TestPersistenceDbContext : TenantScopedDbContext
{
    public TestPersistenceDbContext(DbContextOptions<TestPersistenceDbContext> options, ITenantContext tenantContext)
        : base(options, tenantContext)
    {
    }

    public DbSet<Widget> Widgets => Set<Widget>();

    public DbSet<Badge> Badges => Set<Badge>();

    public DbSet<VersionedWidget> VersionedWidgets => Set<VersionedWidget>();

    public DbSet<AuditableWidget> AuditableWidgets => Set<AuditableWidget>();

    public DbSet<PlainNote> Notes => Set<PlainNote>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        ApplyTenantQueryFilters(modelBuilder);
    }
}

internal sealed class ExemptPersistenceDbContext : TenantScopedDbContext
{
    public ExemptPersistenceDbContext(DbContextOptions<ExemptPersistenceDbContext> options, ITenantContext tenantContext)
        : base(options, tenantContext)
    {
    }

    public DbSet<Badge> Badges => Set<Badge>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        ApplyTenantQueryFilters(modelBuilder);
    }

    protected override bool IsExemptFromCrossTenantValidation(EntityEntry entry)
    {
        return true;
    }
}

internal static class PersistenceTestOptions
{
    public static DbContextOptions<TContext> Build<TContext>(string databaseName, params IInterceptor[] interceptors)
        where TContext : DbContext
    {
        DbContextOptionsBuilder<TContext> builder = new DbContextOptionsBuilder<TContext>()
            .UseInMemoryDatabase(databaseName);

        if (interceptors.Length > 0)
        {
            builder.AddInterceptors(interceptors);
        }

        return builder.Options;
    }

    public static string NewDatabaseName(string prefix)
    {
        return prefix + "-" + Guid.NewGuid();
    }
}

internal static class TenantContextMocks
{
    public static Mock<ITenantContext> WithTenant(Guid? tenantId)
    {
        Mock<ITenantContext> mock = new Mock<ITenantContext>();
        mock.Setup(t => t.TenantId).Returns(tenantId);
        return mock;
    }
}
