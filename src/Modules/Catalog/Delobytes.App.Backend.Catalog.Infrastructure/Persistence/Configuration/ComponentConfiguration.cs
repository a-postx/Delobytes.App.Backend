using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Persistence.Configurations;

public class ComponentConfiguration : IEntityTypeConfiguration<Component>
{
    public void Configure(EntityTypeBuilder<Component> builder)
    {
        builder.ToTable("Components");

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

        builder.Property(pc => pc.Category)
            .HasConversion<int>()
            .HasDefaultValue(ComponentCategory.Material)
            .IsRequired();

        builder.Property(pc => pc.IsActive)
            .IsRequired();

        builder.Property(pc => pc.CreatedAt)
            .IsRequired();

        builder.HasIndex(pc => pc.IsActive);

        builder.HasMany(pc => pc.Prices)
            .WithOne(p => p.Component)
            .HasForeignKey(p => p.ComponentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(pc => pc.BomLines)
            .WithOne(b => b.Component)
            .HasForeignKey(b => b.ComponentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
