using System.Linq.Expressions;
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

    /// <summary>
    /// Gets or sets the SystemChannelTemplates entity set.
    /// </summary>
    public DbSet<SystemChannelTemplate> SystemChannelTemplates => Set<SystemChannelTemplate>();

    /// <summary>
    /// Gets or sets the Connections entity set.
    /// </summary>
    public DbSet<Connection> Connections => Set<Connection>();

    /// <summary>
    /// Gets or sets the SyncJobs entity set.
    /// </summary>
    public DbSet<SyncJob> SyncJobs => Set<SyncJob>();

    /// <summary>
    /// Gets or sets the RawApiResponses entity set.
    /// </summary>
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
                    .Property<Guid>("TenantId")
                    .IsRequired();

                modelBuilder.Entity(entityType.ClrType)
                    .HasIndex("TenantId");

                IMutableProperty? tenantIdProperty = entityType.FindProperty("TenantId");
                ParameterExpression parameter = Expression.Parameter(entityType.ClrType, "e");
                MemberExpression tenantIdAccess = Expression.Property(parameter, tenantIdProperty!.PropertyInfo!);
                ConstantExpression currentTenantId = Expression.Constant(_tenantContext.TenantId);
                BinaryExpression comparison = Expression.Equal(tenantIdAccess, currentTenantId);
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
