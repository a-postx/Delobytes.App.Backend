using Delobytes.App.Backend.Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Persistence.Configurations;

public class ProductPackagingComponentConfiguration : IEntityTypeConfiguration<ProductPackagingComponent>
{
    public void Configure(EntityTypeBuilder<ProductPackagingComponent> builder)
    {
        builder.ToTable("ProductPackagingComponents");

        builder.HasKey(ppc => ppc.Id);

        builder.Property(ppc => ppc.ProductId)
            .IsRequired();

        builder.Property(ppc => ppc.PackagingComponentId)
            .IsRequired();

        builder.Property(ppc => ppc.Quantity)
            .HasPrecision(10, 4)
            .IsRequired();

        builder.Property(ppc => ppc.CreatedAt)
            .IsRequired();

        builder.HasIndex(ppc => new { ppc.ProductId, ppc.PackagingComponentId })
            .IsUnique();

        builder.HasOne(ppc => ppc.Product)
            .WithMany(p => p.ProductPackagingComponents)
            .HasForeignKey(ppc => ppc.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(ppc => ppc.PackagingComponent)
            .WithMany(pc => pc.ProductPackagingComponents)
            .HasForeignKey(ppc => ppc.PackagingComponentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
