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
        builder.Property(b => b.IsActive).IsRequired();
        builder.Property(b => b.CreatedAt).IsRequired();
        builder.Property(b => b.UpdatedAt);
        builder.HasIndex(b => new { b.ProductId, b.ComponentId, b.ValidFrom });
        builder.HasIndex(b => new { b.ProductId, b.IsActive });
        builder.HasOne(b => b.Product).WithMany(p => p.BomLines).HasForeignKey(b => b.ProductId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(b => b.Component).WithMany(c => c.BomLines).HasForeignKey(b => b.ComponentId).OnDelete(DeleteBehavior.Restrict);
    }
}
