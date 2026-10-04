using Delobytes.App.Backend.Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Persistence.Configuration;

public class ProductCostSnapshotConfiguration : IEntityTypeConfiguration<ProductCostSnapshot>
{
    public void Configure(EntityTypeBuilder<ProductCostSnapshot> builder)
    {
        builder.ToTable("ProductCostSnapshots");
        builder.HasKey(snapshot => snapshot.Id);
        builder.Property(snapshot => snapshot.AsOfDate).IsRequired();
        builder.Property(snapshot => snapshot.MaterialCost).HasPrecision(18, 4).IsRequired();
        builder.Property(snapshot => snapshot.LogisticsCost).HasPrecision(18, 4).IsRequired();
        builder.Property(snapshot => snapshot.PackagingCost).HasPrecision(18, 4).IsRequired();
        builder.Property(snapshot => snapshot.LaborCost).HasPrecision(18, 4).IsRequired();
        builder.Property(snapshot => snapshot.TotalCost).HasPrecision(18, 4).IsRequired();
        builder.Property(snapshot => snapshot.IsComplete).IsRequired();
        builder.Property(snapshot => snapshot.LinesSnapshotJson).IsRequired();
        builder.Property(snapshot => snapshot.TriggerReason).HasMaxLength(64).IsRequired();
        builder.Property(snapshot => snapshot.CalculatedAt).IsRequired();
        builder.HasIndex(snapshot => new { snapshot.ProductId, snapshot.AsOfDate });
        builder.HasOne(snapshot => snapshot.Product)
            .WithMany()
            .HasForeignKey(snapshot => snapshot.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
