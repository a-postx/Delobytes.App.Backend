using Delobytes.App.Backend.Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="ProductPhoto"/>.
/// </summary>
public class ProductPhotoConfiguration : IEntityTypeConfiguration<ProductPhoto>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<ProductPhoto> builder)
    {
        builder.ToTable("ProductPhotos");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.DisplayOrder)
            .IsRequired();

        builder.Property(p => p.SizeVariant)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(p => p.StorageKey)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(p => p.OriginalFileName)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(p => p.ContentType)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(p => p.SizeBytes)
            .IsRequired();

        builder.Property(p => p.Status)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(p => p.Source)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(p => p.ExternalId)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(p => p.ErrorMessage)
            .HasMaxLength(1000);

        builder.Property(p => p.CreatedAt)
            .IsRequired();

        // Re-imports always filter by (ProductId, ExternalId) to decide what is already there,
        // so the lookup index leads with ProductId; the unique constraint below covers the write path.
        builder.HasIndex(p => new { p.ProductId, p.ExternalId });
        builder.HasIndex(p => new { p.ProductId, p.DisplayOrder, p.SizeVariant });

        // Idempotency is enforced by the database, not only by a check in code:
        // a concurrent re-import gets a unique-violation instead of a silent duplicate.
        // TenantId is a shadow property, so it is referenced by name.
        builder.HasIndex("TenantId", nameof(ProductPhoto.ProductId), nameof(ProductPhoto.ExternalId))
            .IsUnique()
            .HasDatabaseName("IX_ProductPhotos_TenantId_ProductId_ExternalId");

        builder.HasOne(p => p.Product)
            .WithMany(pr => pr.Photos)
            .HasForeignKey(p => p.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
