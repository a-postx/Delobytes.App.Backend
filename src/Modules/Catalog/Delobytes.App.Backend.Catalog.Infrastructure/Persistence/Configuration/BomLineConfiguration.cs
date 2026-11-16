using Delobytes.App.Backend.Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Persistence.Configurations;

public class BomLineConfiguration : IEntityTypeConfiguration<BomLine>
{
    public void Configure(EntityTypeBuilder<BomLine> builder)
    {
        builder.ToTable("BomLines");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.ProductId).IsRequired();
        builder.Property(b => b.ComponentId).IsRequired();
        builder.Property(b => b.Quantity).HasPrecision(18, 4).IsRequired();
        builder.Property(b => b.ValidFrom).IsRequired();

        // Null means "still in force", so the column is required to be nullable by the model itself,
        // not by data that happens to be complete today.
        builder.Property(b => b.ValidTo);
        builder.Property(b => b.CreatedAt).IsRequired();
        builder.Property(b => b.UpdatedAt);
        builder.HasIndex(b => new { b.ProductId, b.ComponentId, b.ValidFrom });

        // Replaces the index on the former IsActive flag: every lookup that used to filter on the
        // flag now filters on ValidTo being null, and the effective-date lookup bounds it from above.
        builder.HasIndex(b => new { b.ProductId, b.ValidTo });
        builder.HasOne(b => b.Product).WithMany(p => p.BomLines).HasForeignKey(b => b.ProductId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(b => b.Component).WithMany(c => c.BomLines).HasForeignKey(b => b.ComponentId).OnDelete(DeleteBehavior.Restrict);
    }
}
