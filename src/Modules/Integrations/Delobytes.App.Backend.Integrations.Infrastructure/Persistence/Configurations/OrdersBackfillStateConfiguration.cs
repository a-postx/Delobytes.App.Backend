using Delobytes.App.Backend.Integrations.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core entity type configuration for OrdersBackfillState.
/// </summary>
public class OrdersBackfillStateConfiguration : IEntityTypeConfiguration<OrdersBackfillState>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<OrdersBackfillState> builder)
    {
        builder.ToTable("OrdersBackfillStates");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.ConnectionId)
            .IsRequired();

        builder.Property(s => s.NextDayToFetch)
            .IsRequired();

        builder.Property(s => s.OldestDayInScope)
            .IsRequired();

        builder.Property(s => s.IsCompleted)
            .IsRequired();

        builder.Property(s => s.CompletedAt);

        builder.Property(s => s.CatchUpNextDay);

        builder.Property(s => s.HandoverWatermark);

        // One backfill cursor per connection.
        builder.HasIndex(s => s.ConnectionId)
            .IsUnique()
            .HasDatabaseName("IX_OrdersBackfillStates_ConnectionId");

        builder.HasOne(s => s.Connection)
            .WithMany()
            .HasForeignKey(s => s.ConnectionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
