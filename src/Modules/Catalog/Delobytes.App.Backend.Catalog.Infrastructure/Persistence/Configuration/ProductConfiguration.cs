using Delobytes.App.Backend.Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Persistence.Configurations;

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Sku)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(p => p.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(p => p.Description)
            .HasMaxLength(2000);

        builder.Property(p => p.LengthCm)
            .HasPrecision(8, 2)
            .IsRequired();

        builder.Property(p => p.WidthCm)
            .HasPrecision(8, 2)
            .IsRequired();

        builder.Property(p => p.HeightCm)
            .HasPrecision(8, 2)
            .IsRequired();

        builder.Property(p => p.IsActive)
            .IsRequired();

        builder.Property(p => p.CreatedAt)
            .IsRequired();

        builder.Property(p => p.UpdatedAt);

        builder.HasIndex(p => p.Sku)
            .IsUnique();

        builder.HasIndex(p => p.IsActive);

        builder.HasMany(p => p.ChannelProducts)
            .WithOne(cp => cp.Product)
            .HasForeignKey(cp => cp.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.ProductComponents)
            .WithOne(pc => pc.Product)
            .HasForeignKey(pc => pc.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.ProductPackagingComponents)
            .WithOne(ppc => ppc.Product)
            .HasForeignKey(ppc => ppc.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.ProductChannelInputs)
            .WithOne(pci => pci.Product)
            .HasForeignKey(pci => pci.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.ProductWorkRates)
            .WithOne(pwr => pwr.Product)
            .HasForeignKey(pwr => pwr.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
