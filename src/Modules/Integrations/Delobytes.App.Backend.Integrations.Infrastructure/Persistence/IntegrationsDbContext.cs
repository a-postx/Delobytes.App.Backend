using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Persistence;

/// <summary>
/// EF Core DbContext for the Integrations module.
/// </summary>
public class IntegrationsDbContext : TenantScopedDbContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IntegrationsDbContext"/> class.
    /// </summary>
    public IntegrationsDbContext(DbContextOptions<IntegrationsDbContext> options, ITenantContext tenantContext)
        : base(options, tenantContext)
    {
    }

    public DbSet<SystemChannelTemplate> SystemChannelTemplates => Set<SystemChannelTemplate>();
    public DbSet<Connection> Connections => Set<Connection>();
    public DbSet<RawApiResponse> RawApiResponses => Set<RawApiResponse>();
    public DbSet<JobRun> JobRuns => Set<JobRun>();
    public DbSet<JobRunBatchResult> JobRunBatchResults => Set<JobRunBatchResult>();
    public DbSet<ProductsImportJob> ProductsImportJobs => Set<ProductsImportJob>();
    public DbSet<OrdersImportJob> OrdersImportJobs => Set<OrdersImportJob>();
    public DbSet<JobCheckpoint> JobCheckpoints => Set<JobCheckpoint>();
    public DbSet<SyncCheckpoint> SyncCheckpoints => Set<SyncCheckpoint>();
    public DbSet<JobSchedule> JobSchedules => Set<JobSchedule>();
    public DbSet<JobTypeConfiguration> JobTypeConfigurations => Set<JobTypeConfiguration>();
    public DbSet<OrdersBackfillState> OrdersBackfillStates => Set<OrdersBackfillState>();
    public DbSet<WildberriesMethodThrottle> WildberriesMethodThrottles => Set<WildberriesMethodThrottle>();

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema("integrations");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IntegrationsDbContext).Assembly);

        ApplyTenantQueryFilters(modelBuilder);
    }
}
