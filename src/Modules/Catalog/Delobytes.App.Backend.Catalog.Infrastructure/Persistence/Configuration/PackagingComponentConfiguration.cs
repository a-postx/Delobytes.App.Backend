using Delobytes.App.Backend.Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Persistence.Configurations;

public class PackagingComponentConfiguration : IEntityTypeConfiguration<PackagingComponent>
{
    public void Configure(EntityTypeBuilder<PackagingComponent> builder)
    {
        builder.ToTable("PackagingComponents");

        builder.HasKey(pc => pc.Id);

        builder.Property(pc => pc.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(pc => pc.Description)
            .HasMaxLength(1000);

        builder.Property(pc => pc.Unit)
            .IsRequired()
            .HasMaxLength(50)
            .HasConversion<string>();

        builder.Property(pc => pc.PricePerUnit)
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(pc => pc.SupplierId);

        builder.Property(pc => pc.IsActive)
            .IsRequired();

        builder.Property(pc => pc.CreatedAt)
            .IsRequired();

        builder.Property(pc => pc.UpdatedAt);

        builder.HasIndex(pc => pc.IsActive);

        builder.HasIndex(pc => pc.SupplierId);

        builder.HasMany(pc => pc.ProductPackagingComponents)
            .WithOne(ppc => ppc.PackagingComponent)
            .HasForeignKey(ppc => ppc.PackagingComponentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
