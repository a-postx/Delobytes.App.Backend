using Delobytes.App.Backend.Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Persistence.Configurations;

public class PackingUnitConfiguration : IEntityTypeConfiguration<PackingUnit>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<PackingUnit> builder)
    {
        builder.ToTable("PackingUnits");

        builder.HasKey(pu => pu.Id);

        builder.Property(pu => pu.ProductId)
            .IsRequired();

        builder.Property(pu => pu.ChannelId);

        builder.Property(pu => pu.Name)
            .HasMaxLength(200);

        builder.Property(pu => pu.LengthCm)
            .HasPrecision(8, 2)
            .IsRequired();

        builder.Property(pu => pu.WidthCm)
            .HasPrecision(8, 2)
            .IsRequired();

        builder.Property(pu => pu.HeightCm)
            .HasPrecision(8, 2)
            .IsRequired();

        builder.Property(pu => pu.WeightKg)
            .HasPrecision(8, 3);

        builder.Property(pu => pu.IsActive)
            .IsRequired();

        builder.Property(pu => pu.CreatedAt)
            .IsRequired();

        builder.Property(pu => pu.UpdatedAt);

        // One row per product and channel. ProductId is the leading column, so this index also
        // serves every lookup by product and the cascade from Product; a separate single-column
        // index on ProductId would only double the write cost.
        builder.HasIndex(pu => new { pu.ProductId, pu.ChannelId })
            .IsUnique()
            .HasDatabaseName("IX_PackingUnits_Product_Channel");

        // At most one canonical row per product. PostgreSQL treats NULLs as distinct, so the
        // composite index above cannot enforce this bound; a filtered index can.
        builder.HasIndex(pu => pu.ProductId)
            .IsUnique()
            .HasFilter("\"ChannelId\" IS NULL")
            .HasDatabaseName("IX_PackingUnits_Product_Canonical");

        builder.HasOne(pu => pu.Product)
            .WithMany(p => p.PackingUnits)
            .HasForeignKey(pu => pu.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Channel>()
            .WithMany()
            .HasForeignKey(pu => pu.ChannelId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
