using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Persistence;

/// <summary>
/// EF Core DbContext for the Catalog module.
/// </summary>
public class CatalogDbContext : TenantScopedDbContext
{
    public CatalogDbContext(DbContextOptions<CatalogDbContext> options, ITenantContext tenantContext)
        : base(options, tenantContext)
    {
    }

    public DbSet<Channel> Channels => Set<Channel>();
    public DbSet<ChannelParameterSet> ChannelParameterSets => Set<ChannelParameterSet>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ChannelProduct> ChannelProducts => Set<ChannelProduct>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<Component> Components => Set<Component>();
    public DbSet<ComponentPrice> ComponentPrices => Set<ComponentPrice>();
    public DbSet<BomLine> BomLines => Set<BomLine>();
    public DbSet<PackingUnit> PackingUnits => Set<PackingUnit>();
    public DbSet<ProductBarcode> ProductBarcodes => Set<ProductBarcode>();
    public DbSet<ProductPhoto> ProductPhotos => Set<ProductPhoto>();
    public DbSet<CostType> CostTypes => Set<CostType>();
    public DbSet<ProductChannelCost> ProductChannelCosts => Set<ProductChannelCost>();
    public DbSet<WorkRate> WorkRates => Set<WorkRate>();
    public DbSet<WorkRateVersion> WorkRateVersions => Set<WorkRateVersion>();
    public DbSet<ProductWorkRate> ProductWorkRates => Set<ProductWorkRate>();
    public DbSet<ProductChannelInput> ProductChannelInputs => Set<ProductChannelInput>();
    public DbSet<MarginCalculationSnapshot> MarginCalculationSnapshots => Set<MarginCalculationSnapshot>();
    public DbSet<ProductCostSnapshot> ProductCostSnapshots => Set<ProductCostSnapshot>();

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema("catalog");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CatalogDbContext).Assembly);

        ApplyTenantQueryFilters(modelBuilder);
    }
}
