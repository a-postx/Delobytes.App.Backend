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

        // Serves both lookups by product and the cascade from Product; PostgreSQL
        // does not create indexes for foreign keys automatically.
        builder.HasIndex(pu => pu.ProductId);

        builder.HasOne(pu => pu.Product)
            .WithMany(p => p.PackingUnits)
            .HasForeignKey(pu => pu.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
