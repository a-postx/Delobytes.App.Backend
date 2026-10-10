using Delobytes.App.Backend.Sales.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Delobytes.App.Backend.Sales.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core entity type configuration for OrderLine.
/// </summary>
public class OrderLineConfiguration : IEntityTypeConfiguration<OrderLine>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<OrderLine> builder)
    {
        builder.ToTable("OrderLines");

        builder.HasKey(l => l.Id);

        builder.Property(l => l.OrderId)
            .IsRequired();

        builder.Property(l => l.LineKey)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(l => l.ExternalLineId)
            .HasMaxLength(100);

        builder.Property(l => l.ExternalProductId)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(l => l.OfferId)
            .HasMaxLength(100);

        builder.Property(l => l.ExternalSizeId)
            .HasMaxLength(100);

        builder.Property(l => l.ProductName)
            .HasMaxLength(500);

        builder.Property(l => l.Vat)
            .HasMaxLength(32);

        builder.Property(l => l.Quantity)
            .IsRequired();

        builder.Property(l => l.UnitPrice)
            .HasPrecision(18, 2);

        builder.Property(l => l.BuyerUnitPrice)
            .HasPrecision(18, 2);

        builder.Property(l => l.UnitPriceBeforeDiscount)
            .HasPrecision(18, 2);

        builder.Property(l => l.Currency)
            .IsRequired()
            .HasMaxLength(3);

        builder.Property(l => l.ChannelProductId);

        builder.Property(l => l.CreatedAt)
            .IsRequired();

        builder.Property(l => l.UpdatedAt);

        // The upsert matches on this key.
        builder.HasIndex("TenantId", nameof(OrderLine.OrderId), nameof(OrderLine.LineKey))
            .IsUnique();

        // Serves the product-deletion guard and "orders for this product". No ChannelId component:
        // ChannelProductId is already unique and encodes the channel.
        builder.HasIndex("TenantId", nameof(OrderLine.ChannelProductId));

        builder.HasOne(l => l.Order)
            .WithMany(o => o.Lines)
            .HasForeignKey(l => l.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(l => l.Settlements)
            .WithOne(s => s.OrderLine)
            .HasForeignKey(s => s.OrderLineId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(l => l.Returns)
            .WithOne(r => r.OrderLine)
            .HasForeignKey(r => r.OrderLineId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
