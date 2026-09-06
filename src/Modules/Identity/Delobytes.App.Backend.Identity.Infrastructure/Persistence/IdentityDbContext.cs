using System.Linq.Expressions;
using System.Reflection;
using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Domain.Entities;
using Delobytes.App.Backend.Identity.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Delobytes.App.Backend.Identity.Infrastructure.Persistence;

/// <summary>
/// EF Core DbContext for the Identity module.
/// Contains Identity-specific entity sets (Tenants, Users, TenantMemberships).
/// </summary>
public class IdentityDbContext : DbContext
{
    private readonly ITenantContext _tenantContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="IdentityDbContext"/> class.
    /// </summary>
    /// <param name="options">DbContext options.</param>
    /// <param name="tenantContext">Tenant context for query filtering.</param>
    public IdentityDbContext(DbContextOptions<IdentityDbContext> options, ITenantContext tenantContext)
        : base(options)
    {
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// Gets or sets the Tenants entity set.
    /// </summary>
    public DbSet<Tenant> Tenants => Set<Tenant>();

    /// <summary>
    /// Gets or sets the Users entity set.
    /// </summary>
    public DbSet<User> Users => Set<User>();

    /// <summary>
    /// Gets or sets the TenantMemberships entity set.
    /// </summary>
    public DbSet<TenantMembership> TenantMemberships => Set<TenantMembership>();

    /// <summary>
    /// Gets or sets the Invitations entity set.
    /// </summary>
    public DbSet<Invitation> Invitations => Set<Invitation>();

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply all entity type configurations from this assembly
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IdentityDbContext).Assembly);

        // Schema isolation for Identity module
        modelBuilder.HasDefaultSchema("identity");

        // Configure shadow property TenantId for tenant-scoped entities
        foreach (IMutableEntityType entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(ITenantScoped).IsAssignableFrom(entityType.ClrType))
            {
                modelBuilder.Entity(entityType.ClrType)
                    .Property<Guid>("TenantId")
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

                ConstantExpression tenantContextConstant = Expression.Constant(_tenantContext, typeof(ITenantContext));
                MemberExpression tenantIdProperty = Expression.Property(tenantContextConstant, nameof(ITenantContext.TenantId));

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
        return base.SaveChanges();
    }

    /// <inheritdoc/>
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SetTenantId();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void SetTenantId()
    {
        Guid? tenantId = _tenantContext.TenantId;
        if (!tenantId.HasValue)
        {
            ////throw new InvalidOperationException("TenantId is not found.");
            return;
        }

        // Set TenantId for all added tenant-scoped entities
        IEnumerable<EntityEntry> entries = ChangeTracker.Entries()
            .Where(e => e.State == EntityState.Added && e.Entity is ITenantScoped);

        foreach (EntityEntry entry in entries)
        {
            entry.Property("TenantId").CurrentValue = tenantId.Value;
        }
    }
}
