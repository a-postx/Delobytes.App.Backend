using Delobytes.App.Backend.Sales.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Delobytes.App.Backend.Sales.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core entity type configuration for OrderSettlement.
/// </summary>
public class OrderSettlementConfiguration : IEntityTypeConfiguration<OrderSettlement>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<OrderSettlement> builder)
    {
        builder.ToTable("OrderSettlements");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.OrderLineId)
            .IsRequired();

        builder.Property(s => s.CommissionAmount)
            .HasPrecision(18, 2);

        // Declared explicitly: without the conversion EF would store the enum as int and the
        // schema would diverge from the other modules, which persist enums as their names.
        builder.Property(s => s.CommissionSource)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(s => s.PayoutAmount)
            .HasPrecision(18, 2);

        builder.Property(s => s.PayoutSource)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(s => s.DeliveryFeeAmount)
            .HasPrecision(18, 2);

        builder.Property(s => s.DeliveryFeeSource)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(s => s.RefundAmount)
            .HasPrecision(18, 2);

        builder.Property(s => s.RefundSource)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(s => s.NetRevenueAmount)
            .HasPrecision(18, 2);

        builder.Property(s => s.NetRevenueSource)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(s => s.State)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(s => s.Currency)
            .IsRequired()
            .HasMaxLength(3);

        builder.Property(s => s.FinancialPeriodFrom);
        builder.Property(s => s.FinancialPeriodTo);

        builder.Property(s => s.RawDataId);
        builder.Property(s => s.ResolvedAt);

        builder.Property(s => s.CreatedAt)
            .IsRequired();

        builder.Property(s => s.UpdatedAt);

        builder.HasIndex("TenantId", nameof(OrderSettlement.OrderLineId))
            .IsUnique();

        builder.HasOne(s => s.OrderLine)
            .WithMany(l => l.Settlements)
            .HasForeignKey(s => s.OrderLineId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
