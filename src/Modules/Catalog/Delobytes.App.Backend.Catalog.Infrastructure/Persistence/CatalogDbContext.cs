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
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<Component> Components => Set<Component>();
    public DbSet<ComponentPrice> ComponentPrices => Set<ComponentPrice>();
    public DbSet<ProductComponent> ProductComponents => Set<ProductComponent>();
    public DbSet<PackingUnit> PackingUnits => Set<PackingUnit>();
    public DbSet<ProductBarcode> ProductBarcodes => Set<ProductBarcode>();
    public DbSet<CostType> CostTypes => Set<CostType>();
    public DbSet<ProductChannelCost> ProductChannelCosts => Set<ProductChannelCost>();
    public DbSet<WorkRate> WorkRates => Set<WorkRate>();
    public DbSet<ProductWorkRate> ProductWorkRates => Set<ProductWorkRate>();
    public DbSet<ProductChannelInput> ProductChannelInputs => Set<ProductChannelInput>();
    public DbSet<MarginCalculationSnapshot> MarginCalculationSnapshots => Set<MarginCalculationSnapshot>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Conventions.Add(_ => new TenantIdShadowPropertyConvention());
    }

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema("catalog");

        foreach (IMutableEntityType entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(ITenantScoped).IsAssignableFrom(entityType.ClrType))
            {
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

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CatalogDbContext).Assembly);
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

    // Guards against orphan rows: without this check, a missing tenant (e.g. a background
    // job or message consumer with no HttpContext) would leave the shadow TenantId at its
    // CLR default (Guid.Empty) instead of throwing, silently creating a row no tenant can
    // ever see through the query filter.
    private void SetTenantId()
    {
        List<EntityEntry> addedScopedEntries = ChangeTracker.Entries()
            .Where(e => e.State == EntityState.Added && e.Entity is ITenantScoped)
            .ToList();

        if (addedScopedEntries.Count == 0)
        {
            return;
        }

        Guid? tenantId = _tenantContext.TenantId;

        if (!tenantId.HasValue)
        {
            string entityNames = string.Join(", ", addedScopedEntries.Select(e => e.Entity.GetType().Name).Distinct());
            throw new SecurityException(
                $"Cannot save tenant-scoped entit{(addedScopedEntries.Count == 1 ? "y" : "ies")} ({entityNames}) " +
                "without a resolved TenantId. The current execution context has no tenant.");
        }

        foreach (EntityEntry entry in addedScopedEntries)
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
