using System.Linq.Expressions;
using System.Reflection;
using System.Security;
using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Domain.Interfaces;
using Delobytes.App.Backend.Sales.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Delobytes.App.Backend.Sales.Infrastructure.Persistence;

/// <summary>
/// EF Core DbContext for the Sales module.
/// </summary>
public class SalesDbContext : DbContext
{
    private readonly ITenantContext _tenantContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="SalesDbContext"/> class.
    /// </summary>
    /// <param name="options">DbContext options.</param>
    /// <param name="tenantContext">Tenant context for query filtering.</param>
    public SalesDbContext(DbContextOptions<SalesDbContext> options, ITenantContext tenantContext)
        : base(options)
    {
        _tenantContext = tenantContext;
    }

    public DbSet<Order> Orders => Set<Order>();
    public DbSet<Return> Returns => Set<Return>();

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SalesDbContext).Assembly);

        modelBuilder.HasDefaultSchema("sales");

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

                // Expression.Constant(this) captures the DbContext instance reference.
                // EF Core recognises DbContext-typed constants in query filter trees and
                // substitutes the *current* instance at query execution time, so TenantId
                // is read from the live scoped ITenantContext on every request — not frozen
                // to the value present when the singleton model cache was first built.
                ConstantExpression contextRef = Expression.Constant(this, typeof(SalesDbContext));
                FieldInfo tenantContextField = typeof(SalesDbContext)
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

    // Prevents writes to entities belonging to a different tenant.
    // Added entities are already protected by SetTenantId() overwriting the value.
    // This guard targets Modified and Deleted: an entity loaded via IgnoreQueryFilters()
    // or with a manually-tampered shadow property would otherwise pass through undetected.
    private void ValidateCrossTenantWrite()
    {
        Guid? tenantId = _tenantContext.TenantId;
        if (!tenantId.HasValue)
        {
            // System / background context — no user-scoped enforcement.
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
