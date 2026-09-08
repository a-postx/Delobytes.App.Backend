using System.Linq.Expressions;
using System.Reflection;
using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Domain.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Persistence;

/// <summary>
/// EF Core DbContext for the Integrations module.
/// </summary>
public class IntegrationsDbContext : DbContext
{
    private readonly ITenantContext _tenantContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="IntegrationsDbContext"/> class.
    /// </summary>
    /// <param name="options">DbContext options.</param>
    /// <param name="tenantContext">Tenant context for query filtering.</param>
    public IntegrationsDbContext(DbContextOptions<IntegrationsDbContext> options, ITenantContext tenantContext)
        : base(options)
    {
        _tenantContext = tenantContext;
    }

    public DbSet<SystemChannelTemplate> SystemChannelTemplates => Set<SystemChannelTemplate>();
    public DbSet<Connection> Connections => Set<Connection>();
    public DbSet<SyncJob> SyncJobs => Set<SyncJob>();
    public DbSet<RawApiResponse> RawApiResponses => Set<RawApiResponse>();

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IntegrationsDbContext).Assembly);

        modelBuilder.HasDefaultSchema("integrations");

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
                ConstantExpression contextRef = Expression.Constant(this, typeof(IntegrationsDbContext));
                FieldInfo tenantContextField = typeof(IntegrationsDbContext)
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
            return;
        }

        IEnumerable<EntityEntry> entries = ChangeTracker.Entries()
            .Where(e => e.State == EntityState.Added && e.Entity is ITenantScoped);

        foreach (EntityEntry entry in entries)
        {
            entry.Property("TenantId").CurrentValue = tenantId.Value;
        }
    }
}
