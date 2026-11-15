using Delobytes.App.Backend.Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="ChannelProductPhoto"/>.
/// </summary>
public class ChannelProductPhotoConfiguration : IEntityTypeConfiguration<ChannelProductPhoto>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<ChannelProductPhoto> builder)
    {
        builder.ToTable("ChannelProductPhotos");

        builder.HasKey(cpp => cpp.Id);

        builder.Property(cpp => cpp.ChannelProductId)
            .IsRequired();

        builder.Property(cpp => cpp.ExternalId)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(cpp => cpp.DisplayOrder)
            .IsRequired();

        builder.Property(cpp => cpp.SizeVariant)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(cpp => cpp.StorageKey)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(cpp => cpp.ContentHash)
            .HasMaxLength(64);

        builder.Property(cpp => cpp.ContentType)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(cpp => cpp.SizeBytes)
            .IsRequired();

        builder.Property(cpp => cpp.Source)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(cpp => cpp.Status)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(cpp => cpp.ErrorMessage)
            .HasMaxLength(1000);

        builder.Property(cpp => cpp.CreatedAt)
            .IsRequired();

        // One live row per slot of a card. The filter keeps the superseded row of a replaced slot
        // from blocking the new one.
        builder.HasIndex(
                "TenantId",
                nameof(ChannelProductPhoto.ChannelProductId),
                nameof(ChannelProductPhoto.ExternalId))
            .IsUnique()
            .HasFilter("\"Status\" <> 3")
            .HasDatabaseName("IX_ChannelProductPhotos_TenantId_ChannelProductId_ExternalId");

        // Supports the sweeper's check for a blob still referenced by a live channel row. There is
        // deliberately no (TenantId, ProductId) index: ownership goes through ChannelProductId.
        builder.HasIndex("TenantId", nameof(ChannelProductPhoto.ContentHash))
            .HasDatabaseName("IX_ChannelProductPhotos_TenantId_ContentHash");

        builder.HasOne(cpp => cpp.ChannelProduct)
            .WithMany(cp => cp.Photos)
            .HasForeignKey(cpp => cpp.ChannelProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
