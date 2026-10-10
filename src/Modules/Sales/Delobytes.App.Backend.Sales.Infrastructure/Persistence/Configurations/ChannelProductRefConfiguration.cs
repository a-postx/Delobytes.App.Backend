using Delobytes.App.Backend.Sales.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Delobytes.App.Backend.Sales.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core entity type configuration for ChannelProductRef.
/// </summary>
public class ChannelProductRefConfiguration : IEntityTypeConfiguration<ChannelProductRef>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<ChannelProductRef> builder)
    {
        builder.ToTable("ChannelProductRefs");

        // The key mirrors the catalogue identifier so resolution needs no surrogate lookup.
        builder.HasKey(r => r.ChannelProductId);

        builder.Property(r => r.ProductId)
            .IsRequired();

        builder.Property(r => r.ChannelId)
            .IsRequired();

        builder.Property(r => r.ExternalProductId)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(r => r.ExternalSku)
            .HasMaxLength(100);

        builder.Property(r => r.Sku)
            .HasMaxLength(100);

        builder.Property(r => r.ProductName)
            .HasMaxLength(500);

        builder.Property(r => r.PhotoUrl)
            .HasMaxLength(1000);

        builder.Property(r => r.IsActive)
            .IsRequired();

        builder.Property(r => r.UpdatedAt)
            .IsRequired();

        // The natural key: this is what the order mapper looks a line up by.
        builder.HasIndex("TenantId", nameof(ChannelProductRef.ChannelId), nameof(ChannelProductRef.ExternalProductId))
            .IsUnique();

        builder.HasIndex("TenantId", nameof(ChannelProductRef.ProductId));
    }
}
