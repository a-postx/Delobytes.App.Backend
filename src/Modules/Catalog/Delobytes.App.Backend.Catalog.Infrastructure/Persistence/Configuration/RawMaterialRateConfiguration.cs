using Delobytes.App.Backend.Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Persistence.Configurations;

public class RawMaterialRateConfiguration : IEntityTypeConfiguration<RawMaterialRate>
{
    public void Configure(EntityTypeBuilder<RawMaterialRate> builder)
    {
        builder.ToTable("RawMaterialRates");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.ProductId)
            .IsRequired();

        builder.Property(r => r.CostPerUnit)
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(r => r.ValidFrom)
            .IsRequired();

        builder.Property(r => r.CreatedAt)
            .IsRequired();

        builder.HasIndex(r => new { r.ProductId, r.ValidFrom });

        builder.HasOne(r => r.Product)
            .WithMany(p => p.RawMaterialRates)
            .HasForeignKey(r => r.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
