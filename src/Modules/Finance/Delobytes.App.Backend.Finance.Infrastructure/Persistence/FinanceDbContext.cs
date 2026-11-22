using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Finance.Domain.Entities;
using Delobytes.App.Backend.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Delobytes.App.Backend.Finance.Infrastructure.Persistence;

/// <summary>
/// EF Core DbContext for the Finance module.
/// </summary>
public class FinanceDbContext : TenantScopedDbContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FinanceDbContext"/> class.
    /// </summary>
    /// <param name="options">DbContext options.</param>
    /// <param name="tenantContext">Tenant context for query filtering.</param>
    public FinanceDbContext(DbContextOptions<FinanceDbContext> options, ITenantContext tenantContext)
        : base(options, tenantContext)
    {
    }

    /// <summary>
    /// Gets the P&amp;L lines resolved by the module.
    /// </summary>
    public DbSet<PnlLine> PnlLines => Set<PnlLine>();

    /// <summary>
    /// Gets the immutable journal of channel money facts.
    /// </summary>
    public DbSet<PnlEntry> PnlEntries => Set<PnlEntry>();

    /// <summary>
    /// Gets the projection of Sales order lines the journal is attributed against.
    /// </summary>
    public DbSet<OrderLineRef> OrderLineRefs => Set<OrderLineRef>();

    /// <summary>
    /// Gets the production costs resolved by Catalog.
    /// </summary>
    public DbSet<OrderLineCost> OrderLineCosts => Set<OrderLineCost>();

    /// <summary>
    /// Gets the expected documents of the sales whose full set has not arrived yet.
    /// </summary>
    public DbSet<PnlLineExpectation> PnlLineExpectations => Set<PnlLineExpectation>();

    /// <summary>
    /// Gets the per-channel delivery checkpoints of the finance feed.
    /// </summary>
    public DbSet<PnlFeedState> PnlFeedStates => Set<PnlFeedState>();


    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema("finance");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FinanceDbContext).Assembly);

        ApplyTenantQueryFilters(modelBuilder);
    }
}
