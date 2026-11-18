using Delobytes.App.Backend.Finance.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Delobytes.App.Backend.Finance.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core entity type configuration for <see cref="PnlLine"/>.
/// </summary>
public class PnlLineConfiguration : IEntityTypeConfiguration<PnlLine>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<PnlLine> builder)
    {
        builder.ToTable("PnlLines");

        builder.HasKey(line => line.Id);

        builder.Property(line => line.OrderLineId)
            .IsRequired();

        builder.Property(line => line.RevenueAmount)
            .HasPrecision(18, 2);

        builder.Property(line => line.RevenueSource)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(line => line.CommissionAmount)
            .HasPrecision(18, 2);

        // Declared explicitly: without the conversion EF would store the enum as int and the
        // schema would diverge from the other modules, which persist enums as their names.
        builder.Property(line => line.CommissionSource)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(line => line.AcquiringAmount)
            .HasPrecision(18, 2);

        builder.Property(line => line.AcquiringSource)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(line => line.DeliveryFeeAmount)
            .HasPrecision(18, 2);

        builder.Property(line => line.DeliveryFeeSource)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(line => line.RefundAmount)
            .HasPrecision(18, 2);

        builder.Property(line => line.RefundSource)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(line => line.CompensationAmount)
            .HasPrecision(18, 2);

        builder.Property(line => line.CompensationSource)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(line => line.NetRevenueAmount)
            .HasPrecision(18, 2);

        builder.Property(line => line.NetRevenueSource)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(line => line.ControlTotalAmount)
            .HasPrecision(18, 2);

        builder.Property(line => line.ControlDifferenceAmount)
            .HasPrecision(18, 2);

        builder.Property(line => line.EntryCount)
            .IsRequired();

        builder.Property(line => line.ProductionCostAmount)
            .HasPrecision(18, 2);

        builder.Property(line => line.ProductionCostIsComplete);

        builder.Property(line => line.SaleDate);

        builder.Property(line => line.ComputedAt);

        builder.Property(line => line.State)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(line => line.Currency)
            .IsRequired()
            .HasMaxLength(3);

        builder.Property(line => line.FinancialPeriodFrom);
        builder.Property(line => line.FinancialPeriodTo);

        builder.Property(line => line.ResolvedAt);

        builder.Property(line => line.CreatedAt)
            .IsRequired();

        builder.Property(line => line.UpdatedAt);

        // One P&L line per Sales order line. The database cannot enforce the reference itself —
        // the order line lives in another module and another schema — so this index is what keeps
        // a re-run of the resolver from producing a second row.
        builder.HasIndex("TenantId", nameof(PnlLine.OrderLineId))
            .IsUnique();
    }
}
