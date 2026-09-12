using Delobytes.App.Backend.Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Persistence.Configurations;

public class ProductWorkRateConfiguration : IEntityTypeConfiguration<ProductWorkRate>
{
    public void Configure(EntityTypeBuilder<ProductWorkRate> builder)
    {
        builder.ToTable("ProductWorkRates");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.ProductId)
            .IsRequired();

        builder.Property(r => r.AssemblyRatePerDay)
            .IsRequired();

        builder.Property(r => r.ValidFrom)
            .IsRequired();

        builder.Property(r => r.IsActive)
            .IsRequired();

        builder.Property(r => r.CreatedAt)
            .IsRequired();

        builder.Property(r => r.UpdatedAt);

        builder.HasIndex(r => new { r.ProductId, r.ValidFrom });

        builder.HasIndex(r => r.IsActive);

        builder.HasOne(r => r.Product)
            .WithMany(p => p.ProductWorkRates)
            .HasForeignKey(r => r.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
