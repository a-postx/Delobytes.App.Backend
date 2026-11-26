using Delobytes.App.Backend.Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Persistence.Configurations;

public class ComponentPriceConfiguration : IEntityTypeConfiguration<ComponentPrice>
{
    public void Configure(EntityTypeBuilder<ComponentPrice> builder)
    {
        builder.ToTable("ComponentPrices");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.ComponentId)
            .IsRequired();

        builder.Property(p => p.PricePerUnit)
            .HasPrecision(18, 4)
            .IsRequired();

        // Six decimals: a pack may hold a fraction of a unit (0,004 kg), and the price is divided
        // by this value, so truncating it would shift the per-unit cost.
        builder.Property(p => p.Quantity)
            .HasPrecision(18, 6)
            .IsRequired();

        builder.Property(p => p.SupplierId);

        builder.Property(p => p.ValidFrom)
            .IsRequired();

        builder.Property(p => p.IsActive)
            .IsRequired();

        builder.Property(p => p.CreatedAt)
            .IsRequired();

        builder.Property(p => p.UpdatedAt);

        // Composite index over tenant, component and effective date, with the shadow TenantId as
        // the leading column. Explicit names keep identifiers inside PostgreSQL's 63-character limit.
        builder.HasIndex("TenantId", nameof(ComponentPrice.ComponentId), nameof(ComponentPrice.ValidFrom))
            .HasDatabaseName("IX_ComponentPrices_TenantId_ComponentId_ValidFrom");

        builder.HasIndex(p => p.IsActive)
            .HasDatabaseName("IX_ComponentPrices_IsActive");

        builder.HasOne(p => p.Component)
            .WithMany(pc => pc.Prices)
            .HasForeignKey(p => p.ComponentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(p => p.Supplier)
            .WithMany(s => s.ComponentPrices)
            .HasForeignKey(p => p.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
