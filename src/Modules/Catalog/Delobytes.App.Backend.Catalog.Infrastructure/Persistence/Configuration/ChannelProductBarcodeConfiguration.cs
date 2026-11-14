using Delobytes.App.Backend.Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="ChannelProductBarcode"/>.
/// </summary>
public class ChannelProductBarcodeConfiguration : IEntityTypeConfiguration<ChannelProductBarcode>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<ChannelProductBarcode> builder)
    {
        builder.ToTable("ChannelProductBarcodes");

        builder.HasKey(cpb => cpb.Id);

        builder.Property(cpb => cpb.ChannelProductId)
            .IsRequired();

        builder.Property(cpb => cpb.Value)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(cpb => cpb.Type)
            .HasMaxLength(50);

        builder.Property(cpb => cpb.CreatedAt)
            .IsRequired();

        // One card cannot report the same value twice; that is a defect of the card, not a case
        // the catalogue has to store. The bound is per card on purpose: unlike
        // IX_ProductBarcodes_TenantId_Value it says nothing about other cards, because a channel
        // may legitimately report one barcode for two products.
        builder.HasIndex("TenantId", nameof(ChannelProductBarcode.ChannelProductId), nameof(ChannelProductBarcode.Value))
            .IsUnique()
            .HasDatabaseName("IX_ChannelProductBarcodes_TenantId_ChannelProductId_Value");

        // Serves the import's match step, which looks a card's barcode up against every card of
        // one channel before falling back to the canonical barcodes.
        builder.HasIndex("TenantId", nameof(ChannelProductBarcode.Value))
            .HasDatabaseName("IX_ChannelProductBarcodes_TenantId_Value");

        builder.HasOne(cpb => cpb.ChannelProduct)
            .WithMany(cp => cp.Barcodes)
            .HasForeignKey(cpb => cpb.ChannelProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
