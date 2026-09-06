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

        builder.Property(o => o.ExternalOrderId)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(o => o.ChannelProductId)
            .IsRequired();

        builder.Property(o => o.ChannelId)
            .IsRequired();

        builder.Property(o => o.OrderDate)
            .IsRequired();

        builder.Property(o => o.Quantity)
            .IsRequired();

        builder.Property(o => o.Revenue)
            .IsRequired()
            .HasPrecision(18, 2);

        builder.Property(o => o.Commission)
            .IsRequired()
            .HasPrecision(18, 2);

        builder.Property(o => o.NetRevenue)
            .IsRequired()
            .HasPrecision(18, 2);

        builder.Property(o => o.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(o => o.ImportedAt)
            .IsRequired();

        builder.Property(o => o.CreatedAt)
            .IsRequired();

        builder.HasIndex(o => new { o.ExternalOrderId, o.ChannelId })
            .IsUnique();

        builder.HasIndex(o => o.ChannelProductId);
        builder.HasIndex(o => o.ChannelId);
        builder.HasIndex(o => o.OrderDate);
        builder.HasIndex(o => o.Status);

        builder.HasOne(o => o.ChannelProduct)
            .WithMany()
            .HasForeignKey(o => o.ChannelProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(o => o.Channel)
            .WithMany()
            .HasForeignKey(o => o.ChannelId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(o => o.RawData)
            .WithMany()
            .HasForeignKey(o => o.RawDataId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(o => o.Returns)
            .WithOne(r => r.Order)
            .HasForeignKey(r => r.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
