using System.Linq.Expressions;
using System.Reflection;
using System.Security;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Contracts.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Persistence;

/// <summary>
/// EF Core DbContext for the Catalog module.
/// </summary>
public class CatalogDbContext : DbContext
{
    private readonly ITenantContext _tenantContext;

    public CatalogDbContext(DbContextOptions<CatalogDbContext> options, ITenantContext tenantContext)
        : base(options)
    {
        _tenantContext = tenantContext;
    }

    public DbSet<Channel> Channels => Set<Channel>();
    public DbSet<ChannelParameterSet> ChannelParameterSets => Set<ChannelParameterSet>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ChannelProduct> ChannelProducts => Set<ChannelProduct>();
    public DbSet<PackagingComponent> PackagingComponents => Set<PackagingComponent>();
    public DbSet<ProductPackagingComponent> ProductPackagingComponents => Set<ProductPackagingComponent>();
    public DbSet<TariffGrid> TariffGrids => Set<TariffGrid>();
    public DbSet<TariffGridEntry> TariffGridEntries => Set<TariffGridEntry>();
    public DbSet<WorkRate> WorkRates => Set<WorkRate>();
    public DbSet<ProductChannelInput> ProductChannelInputs => Set<ProductChannelInput>();
    public DbSet<MarginCalculationSnapshot> MarginCalculationSnapshots => Set<MarginCalculationSnapshot>();
    public DbSet<RawMaterialRate> RawMaterialRates => Set<RawMaterialRate>();

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CatalogDbContext).Assembly);

        modelBuilder.HasDefaultSchema("catalog");

        foreach (IMutableEntityType entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(ITenantScoped).IsAssignableFrom(entityType.ClrType))
            {
                modelBuilder.Entity(entityType.ClrType)
                    .Property<Guid?>("TenantId")
                    .IsRequired();

                modelBuilder.Entity(entityType.ClrType)
                    .HasIndex("TenantId");

                ParameterExpression parameter = Expression.Parameter(entityType.ClrType, "e");

                MethodInfo efPropertyMethod = typeof(EF)
                    .GetMethod(nameof(EF.Property), BindingFlags.Static | BindingFlags.Public)!
                    .MakeGenericMethod(typeof(Guid?));

                MethodCallExpression tenantIdAccess = Expression.Call(
                    efPropertyMethod,
                    parameter,
                    Expression.Constant("TenantId"));

                // EF Core resolves the DbContext instance at query time, not at model-build time,
                // so TenantId always reflects the current scoped ITenantContext.
                ConstantExpression contextRef = Expression.Constant(this, typeof(CatalogDbContext));
                FieldInfo tenantContextField = typeof(CatalogDbContext)
                    .GetField("_tenantContext", BindingFlags.NonPublic | BindingFlags.Instance)!;
                MemberExpression tenantContextAccess = Expression.Field(contextRef, tenantContextField);
                MemberExpression tenantIdProperty = Expression.Property(
                    tenantContextAccess,
                    nameof(ITenantContext.TenantId));

                BinaryExpression comparison = Expression.Equal(tenantIdAccess, tenantIdProperty);
                LambdaExpression lambda = Expression.Lambda(comparison, parameter);

                modelBuilder.Entity(entityType.ClrType).HasQueryFilter(lambda);
            }
        }
    }

    /// <inheritdoc/>
    public override int SaveChanges()
    {
        SetTenantId();
        ValidateCrossTenantWrite();
        return base.SaveChanges();
    }

    /// <inheritdoc/>
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SetTenantId();
        ValidateCrossTenantWrite();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void SetTenantId()
    {
        Guid? tenantId = _tenantContext.TenantId;
        if (!tenantId.HasValue)
        {
            return;
        }

        IEnumerable<EntityEntry> entries = ChangeTracker.Entries()
            .Where(e => e.State == EntityState.Added && e.Entity is ITenantScoped);

        foreach (EntityEntry entry in entries)
        {
            entry.Property("TenantId").CurrentValue = tenantId.Value;
        }
    }

    private void ValidateCrossTenantWrite()
    {
        Guid? tenantId = _tenantContext.TenantId;
        if (!tenantId.HasValue)
        {
            return;
        }

        IEnumerable<EntityEntry> entries = ChangeTracker.Entries()
            .Where(e =>
                (e.State == EntityState.Modified || e.State == EntityState.Deleted)
                && e.Entity is ITenantScoped);

        foreach (EntityEntry entry in entries)
        {
            Guid? entityTenantId = (Guid?)entry.Property("TenantId").CurrentValue;

            if (entityTenantId.HasValue && entityTenantId.Value != tenantId.Value)
            {
                throw new SecurityException(
                    $"Cross-tenant write detected on '{entry.Entity.GetType().Name}': " +
                    $"entity TenantId '{entityTenantId}' does not match current tenant '{tenantId}'.");
            }
        }
    }
}
