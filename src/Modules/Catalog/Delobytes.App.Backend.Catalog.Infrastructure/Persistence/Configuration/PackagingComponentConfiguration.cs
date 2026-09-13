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

        builder.Property(pc => pc.IsActive)
            .IsRequired();

        builder.Property(pc => pc.CreatedAt)
            .IsRequired();

        builder.HasIndex(pc => pc.IsActive);

        builder.HasMany(pc => pc.Prices)
            .WithOne(p => p.PackagingComponent)
            .HasForeignKey(p => p.PackagingComponentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(pc => pc.ProductPackagingComponents)
            .WithOne(ppc => ppc.PackagingComponent)
            .HasForeignKey(ppc => ppc.PackagingComponentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
