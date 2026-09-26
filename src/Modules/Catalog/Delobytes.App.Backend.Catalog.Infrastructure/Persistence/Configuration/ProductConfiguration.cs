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

        builder.Property(p => p.Status)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(p => p.ArchivedAt)
            .IsRequired(false);

        builder.Property(p => p.DeletionRequestedAt)
            .IsRequired(false);

        builder.Property(p => p.DeletedAt)
            .IsRequired(false);

        builder.HasIndex(p => p.Status);

        // for monitoring stuck pending deletions
        builder.HasIndex(p => new { p.Status, p.DeletionRequestedAt });

        builder.HasIndex("TenantId", nameof(Product.Sku))
            .IsUnique()
            .HasDatabaseName("IX_Products_TenantId_Sku");

        builder.HasMany(p => p.ChannelProducts)
            .WithOne(cp => cp.Product)
            .HasForeignKey(cp => cp.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.ProductComponents)
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

        builder.HasMany(p => p.PackingUnits)
            .WithOne(pu => pu.Product)
            .HasForeignKey(pu => pu.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
