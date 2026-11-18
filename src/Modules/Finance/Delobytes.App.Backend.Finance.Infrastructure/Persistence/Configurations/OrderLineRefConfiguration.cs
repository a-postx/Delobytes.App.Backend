using Delobytes.App.Backend.Finance.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Delobytes.App.Backend.Finance.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core entity type configuration for <see cref="OrderLineRef"/>.
/// </summary>
public class OrderLineRefConfiguration : IEntityTypeConfiguration<OrderLineRef>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<OrderLineRef> builder)
    {
        builder.ToTable("OrderLineRefs");

        builder.HasKey(r => r.OrderLineId);

        builder.Property(r => r.OrderId)
            .IsRequired();

        builder.Property(r => r.ChannelId)
            .IsRequired();

        builder.Property(r => r.ExternalOrderId)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(r => r.ExternalLineId)
            .HasMaxLength(100);

        builder.Property(r => r.ExternalProductId)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(r => r.ChannelProductId);

        builder.Property(r => r.Quantity)
            .IsRequired();

        builder.Property(r => r.OrderDate)
            .IsRequired();

        builder.Property(r => r.Currency)
            .IsRequired()
            .HasMaxLength(3);

        builder.Property(r => r.IsCancelled)
            .IsRequired();

        builder.Property(r => r.UpdatedAt)
            .IsRequired();

        // Attribution by unit identity: the Wildberries srid or the Ozon posting number.
        builder.HasIndex("TenantId", nameof(OrderLineRef.ChannelId), nameof(OrderLineRef.ExternalLineId));

        // Attribution of an Ozon ITEM accrual, which carries the order and the SKU but no posting.
        builder.HasIndex(
            "TenantId",
            nameof(OrderLineRef.ChannelId),
            nameof(OrderLineRef.ExternalOrderId),
            nameof(OrderLineRef.ExternalProductId));
    }
}
