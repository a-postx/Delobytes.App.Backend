using Delobytes.App.Backend.Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Persistence.Configurations;

public class ProductBarcodeConfiguration : IEntityTypeConfiguration<ProductBarcode>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<ProductBarcode> builder)
    {
        builder.ToTable("ProductBarcodes");

        builder.HasKey(pb => pb.Id);

        builder.Property(pb => pb.ProductId)
            .IsRequired();

        builder.Property(pb => pb.Value)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(pb => pb.Type)
            .HasMaxLength(50);

        builder.Property(pb => pb.IsDefault)
            .IsRequired();

        builder.Property(pb => pb.CreatedAt)
            .IsRequired();

        builder.HasIndex("TenantId", nameof(ProductBarcode.Value))
            .IsUnique()
            .HasDatabaseName("IX_ProductBarcodes_TenantId_Value");

        builder.HasIndex(pb => pb.ProductId);

        builder.HasOne(pb => pb.Product)
            .WithMany(p => p.Barcodes)
            .HasForeignKey(pb => pb.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
