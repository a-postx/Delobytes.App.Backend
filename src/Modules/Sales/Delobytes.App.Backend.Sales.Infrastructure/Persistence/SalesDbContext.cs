using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Persistence;
using Delobytes.App.Backend.Sales.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Delobytes.App.Backend.Sales.Infrastructure.Persistence;

/// <summary>
/// EF Core DbContext for the Sales module.
/// </summary>
public class SalesDbContext : TenantScopedDbContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SalesDbContext"/> class.
    /// </summary>
    /// <param name="options">DbContext options.</param>
    /// <param name="tenantContext">Tenant context for query filtering.</param>
    public SalesDbContext(DbContextOptions<SalesDbContext> options, ITenantContext tenantContext)
        : base(options, tenantContext)
    {
    }

    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();
    public DbSet<OrderSettlement> OrderSettlements => Set<OrderSettlement>();
    public DbSet<Return> Returns => Set<Return>();
    public DbSet<ChannelProductRef> ChannelProductRefs => Set<ChannelProductRef>();

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema("sales");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SalesDbContext).Assembly);

        ApplyTenantQueryFilters(modelBuilder);
    }
}
