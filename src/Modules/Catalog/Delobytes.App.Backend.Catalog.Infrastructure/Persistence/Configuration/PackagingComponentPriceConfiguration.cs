using Delobytes.App.Backend.Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Persistence.Configurations;

public class PackagingComponentPriceConfiguration : IEntityTypeConfiguration<PackagingComponentPrice>
{
    public void Configure(EntityTypeBuilder<PackagingComponentPrice> builder)
    {
        builder.ToTable("PackagingComponentPrices");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.PackagingComponentId)
            .IsRequired();

        builder.Property(p => p.PricePerUnit)
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(p => p.SupplierId);

        builder.Property(p => p.ValidFrom)
            .IsRequired();

        builder.Property(p => p.IsActive)
            .IsRequired();

        builder.Property(p => p.CreatedAt)
            .IsRequired();

        builder.Property(p => p.UpdatedAt);

        // Composite index over tenant, component and effective date (shadow TenantId, as in ChannelParameterSets
        // the leading tenant column). Explicit names keep identifiers inside PostgreSQL's 63-character limit.
        builder.HasIndex("TenantId", nameof(PackagingComponentPrice.PackagingComponentId), nameof(PackagingComponentPrice.ValidFrom))
            .HasDatabaseName("IX_PackagingComponentPrices_TenantId_ComponentId_ValidFrom");

        builder.HasIndex(p => p.IsActive)
            .HasDatabaseName("IX_PackagingComponentPrices_IsActive");

        builder.HasOne(p => p.PackagingComponent)
            .WithMany(pc => pc.Prices)
            .HasForeignKey(p => p.PackagingComponentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(p => p.Supplier)
            .WithMany(s => s.PackagingComponentPrices)
            .HasForeignKey(p => p.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
