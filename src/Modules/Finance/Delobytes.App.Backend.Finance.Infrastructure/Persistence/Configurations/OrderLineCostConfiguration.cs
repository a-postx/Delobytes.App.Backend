using Delobytes.App.Backend.Finance.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Delobytes.App.Backend.Finance.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core entity type configuration for <see cref="OrderLineCost"/>.
/// </summary>
public class OrderLineCostConfiguration : IEntityTypeConfiguration<OrderLineCost>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<OrderLineCost> builder)
    {
        builder.ToTable("OrderLineCosts");

        builder.HasKey(c => c.OrderLineId);

        builder.Property(c => c.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(c => c.ProductId);

        builder.Property(c => c.AsOfDate)
            .IsRequired();

        builder.Property(c => c.Quantity)
            .IsRequired();

        builder.Property(c => c.UnitMaterialCost)
            .HasPrecision(18, 6);

        builder.Property(c => c.UnitLogisticsCost)
            .HasPrecision(18, 6);

        builder.Property(c => c.UnitPackagingCost)
            .HasPrecision(18, 6);

        builder.Property(c => c.UnitLaborCost)
            .HasPrecision(18, 6);

        builder.Property(c => c.UnitTotalCost)
            .HasPrecision(18, 6);

        builder.Property(c => c.TotalCost)
            .HasPrecision(18, 6);

        builder.Property(c => c.IsComplete)
            .IsRequired();

        // Catalog reports warnings by name; storing names keeps Finance independent of the
        // Catalog.Application enum while staying readable in the database.
        builder.Property(c => c.WarningTypes)
            .HasColumnType("text[]");

        builder.Property(c => c.ResolvedAt)
            .IsRequired();

        builder.Property(c => c.CreatedAt)
            .IsRequired();

        builder.Property(c => c.UpdatedAt);
    }
}
