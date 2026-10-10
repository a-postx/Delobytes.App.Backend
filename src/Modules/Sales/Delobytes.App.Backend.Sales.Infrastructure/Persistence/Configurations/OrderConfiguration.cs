using Delobytes.App.Backend.Sales.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Delobytes.App.Backend.Sales.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core entity type configuration for Order.
/// </summary>
public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Orders");

        builder.HasKey(o => o.Id);

        builder.Property(o => o.ChannelId)
            .IsRequired();

        builder.Property(o => o.ConnectionId);

        builder.Property(o => o.ExternalOrderId)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(o => o.ExternalOrderNumber)
            .HasMaxLength(100);

        builder.Property(o => o.OrderDate)
            .IsRequired();

        builder.Property(o => o.StatusChangedAt);

        builder.Property(o => o.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(o => o.ExternalStatus)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(o => o.ExternalSubstatus)
            .HasMaxLength(64);

        builder.Property(o => o.CancelReason)
            .HasMaxLength(200);

        builder.Property(o => o.Currency)
            .IsRequired()
            .HasMaxLength(3);

        builder.Property(o => o.DeliveryCity)
            .HasMaxLength(200);

        builder.Property(o => o.DeliveryRegion)
            .HasMaxLength(200);

        builder.Property(o => o.WarehouseName)
            .HasMaxLength(200);

        builder.Property(o => o.IsSellerWarehouse);
        builder.Property(o => o.IsB2b);
        builder.Property(o => o.IsTestOrder);

        builder.Property(o => o.ChannelReportedTotal)
            .HasPrecision(18, 2);

        builder.Property(o => o.ChannelReportedDeliveryFee)
            .HasPrecision(18, 2);

        builder.Property(o => o.RawDataId);

        builder.Property(o => o.FirstImportedAt)
            .IsRequired();

        builder.Property(o => o.LastImportedAt)
            .IsRequired();

        builder.Property(o => o.CreatedAt)
            .IsRequired();

        builder.Property(o => o.UpdatedAt);

        // RowVersion is mapped to the PostgreSQL xmin system column by the shared
        // RowVersionedEntityConvention, which runs before this configuration.

        // The tenant is part of the identity: two tenants can legitimately receive the same
        // channel order id.
        builder.HasIndex("TenantId", nameof(Order.ChannelId), nameof(Order.ExternalOrderId))
            .IsUnique();

        builder.HasIndex("TenantId", nameof(Order.OrderDate));
        builder.HasIndex("TenantId", nameof(Order.ChannelId), nameof(Order.OrderDate));
        builder.HasIndex("TenantId", nameof(Order.Status));

        // ChannelProduct and Channel live in Catalog, RawData in Integrations.
        // Navigation properties removed — IDs are stored, cross-module joins happen via events/API.
        builder.HasMany(o => o.Lines)
            .WithOne(l => l.Order)
            .HasForeignKey(l => l.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
