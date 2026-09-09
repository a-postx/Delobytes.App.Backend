using Delobytes.App.Backend.Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Persistence.Configurations;

public class MarginCalculationSnapshotConfiguration : IEntityTypeConfiguration<MarginCalculationSnapshot>
{
    public void Configure(EntityTypeBuilder<MarginCalculationSnapshot> builder)
    {
        builder.ToTable("MarginCalculationSnapshots");

        builder.HasKey(mcs => mcs.Id);

        builder.Property(mcs => mcs.ProductChannelInputId)
            .IsRequired();

        builder.Property(mcs => mcs.TariffGridId);

        builder.Property(mcs => mcs.WorkRateId)
            .IsRequired();

        builder.Property(mcs => mcs.RawMaterialCost).HasPrecision(18, 4).IsRequired();
        builder.Property(mcs => mcs.PackagingCost).HasPrecision(18, 4).IsRequired();
        builder.Property(mcs => mcs.PackagingWorkCost).HasPrecision(18, 4).IsRequired();
        builder.Property(mcs => mcs.LogisticsToMarketplaceCost).HasPrecision(18, 4).IsRequired();
        builder.Property(mcs => mcs.TotalCost).HasPrecision(18, 4).IsRequired();
        builder.Property(mcs => mcs.BuyerPrice).HasPrecision(18, 4).IsRequired();
        builder.Property(mcs => mcs.CommissionAmount).HasPrecision(18, 4).IsRequired();
        builder.Property(mcs => mcs.AcquiringAmount).HasPrecision(18, 4).IsRequired();
        builder.Property(mcs => mcs.TaxAmount).HasPrecision(18, 4).IsRequired();
        builder.Property(mcs => mcs.NetRevenue).HasPrecision(18, 4).IsRequired();
        builder.Property(mcs => mcs.Margin).HasPrecision(18, 4).IsRequired();
        builder.Property(mcs => mcs.MarginPercent).HasPrecision(8, 6).IsRequired();

        builder.Property(mcs => mcs.CalculatedAt)
            .IsRequired();

        builder.HasIndex(mcs => mcs.ProductChannelInputId);
        builder.HasIndex(mcs => mcs.CalculatedAt);

        builder.HasOne(mcs => mcs.ProductChannelInput)
            .WithMany(pci => pci.MarginCalculationSnapshots)
            .HasForeignKey(mcs => mcs.ProductChannelInputId)
            .OnDelete(DeleteBehavior.Cascade);

        // WorkRate and TariffGrid are referenced by ID only; no navigation FK to avoid
        // accidental deletion of historical rate records that snapshots depend on.
    }
}
