using Delobytes.App.Backend.Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Persistence.Configurations;

public class ProductComponentConfiguration : IEntityTypeConfiguration<ProductComponent>
{
    public void Configure(EntityTypeBuilder<ProductComponent> builder)
    {
        builder.ToTable("ProductComponents");

        builder.HasKey(ppc => ppc.Id);

        builder.Property(ppc => ppc.ProductId)
            .IsRequired();

        builder.Property(ppc => ppc.ComponentId)
            .IsRequired();

        builder.Property(ppc => ppc.Quantity)
            .HasPrecision(10, 4)
            .IsRequired();

        builder.Property(ppc => ppc.CreatedAt)
            .IsRequired();

        builder.HasIndex(ppc => new { ppc.ProductId, ppc.ComponentId })
            .IsUnique();

        builder.HasOne(ppc => ppc.Product)
            .WithMany(p => p.ProductComponents)
            .HasForeignKey(ppc => ppc.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(ppc => ppc.Component)
            .WithMany(pc => pc.ProductComponents)
            .HasForeignKey(ppc => ppc.ComponentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
