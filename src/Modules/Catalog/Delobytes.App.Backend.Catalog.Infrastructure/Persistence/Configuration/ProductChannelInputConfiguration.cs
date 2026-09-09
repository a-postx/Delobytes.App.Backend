using Delobytes.App.Backend.Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Persistence.Configurations;

public class ProductChannelInputConfiguration : IEntityTypeConfiguration<ProductChannelInput>
{
    public void Configure(EntityTypeBuilder<ProductChannelInput> builder)
    {
        builder.ToTable("ProductChannelInputs");

        builder.HasKey(pci => pci.Id);

        builder.Property(pci => pci.ProductId)
            .IsRequired();

        builder.Property(pci => pci.ChannelParameterSetId)
            .IsRequired();

        builder.Property(pci => pci.RawMaterialCost)
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(pci => pci.LogisticsToCost)
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(pci => pci.PriceWithoutDiscount)
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(pci => pci.ValidFrom)
            .IsRequired();

        builder.Property(pci => pci.CreatedAt)
            .IsRequired();

        builder.HasIndex(pci => new { pci.ProductId, pci.ChannelParameterSetId, pci.ValidFrom });

        builder.HasOne(pci => pci.Product)
            .WithMany(p => p.ProductChannelInputs)
            .HasForeignKey(pci => pci.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(pci => pci.ChannelParameterSet)
            .WithMany(cps => cps.ProductChannelInputs)
            .HasForeignKey(pci => pci.ChannelParameterSetId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(pci => pci.MarginCalculationSnapshots)
            .WithOne(mcs => mcs.ProductChannelInput)
            .HasForeignKey(mcs => mcs.ProductChannelInputId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
