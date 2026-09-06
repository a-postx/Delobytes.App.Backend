using Delobytes.App.Backend.Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core entity type configuration for ChannelProduct.
/// </summary>
public class ChannelProductConfiguration : IEntityTypeConfiguration<ChannelProduct>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<ChannelProduct> builder)
    {
        builder.ToTable("ChannelProducts");

        builder.HasKey(cp => cp.Id);

        builder.Property(cp => cp.ProductId)
            .IsRequired();

        builder.Property(cp => cp.ChannelId)
            .IsRequired();

        builder.Property(cp => cp.ExternalProductId)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(cp => cp.ExternalSku)
            .HasMaxLength(100);

        builder.Property(cp => cp.ChannelSpecificData)
            .HasColumnType("text");

        builder.Property(cp => cp.IsActive)
            .IsRequired();

        builder.Property(cp => cp.LastSyncedAt);

        builder.Property(cp => cp.CreatedAt)
            .IsRequired();

        builder.Property(cp => cp.UpdatedAt);

        builder.HasIndex(cp => new { cp.ProductId, cp.ChannelId })
            .IsUnique();

        builder.HasIndex(cp => cp.ChannelId);
        builder.HasIndex(cp => cp.ExternalProductId);
        builder.HasIndex(cp => cp.IsActive);
        builder.HasIndex(cp => cp.LastSyncedAt);

        builder.HasOne(cp => cp.Product)
            .WithMany(p => p.ChannelProducts)
            .HasForeignKey(cp => cp.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(cp => cp.Channel)
            .WithMany(c => c.ChannelProducts)
            .HasForeignKey(cp => cp.ChannelId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
