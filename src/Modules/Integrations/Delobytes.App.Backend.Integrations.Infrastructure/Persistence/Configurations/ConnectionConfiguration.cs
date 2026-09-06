using Delobytes.App.Backend.Integrations.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core entity type configuration for Connection.
/// </summary>
public class ConnectionConfiguration : IEntityTypeConfiguration<Connection>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<Connection> builder)
    {
        builder.ToTable("Connections");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.ChannelId)
            .IsRequired();

        builder.Property(c => c.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(c => c.ApiKey)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(c => c.ApiSecret)
            .HasMaxLength(500);

        builder.Property(c => c.Settings)
            .HasColumnType("text");

        builder.Property(c => c.IsActive)
            .IsRequired();

        builder.Property(c => c.LastSyncAt);

        builder.Property(c => c.CreatedAt)
            .IsRequired();

        builder.Property(c => c.UpdatedAt);

        builder.HasIndex(c => c.ChannelId);
        builder.HasIndex(c => c.IsActive);
        builder.HasIndex(c => c.LastSyncAt);

        builder.HasOne(c => c.Channel)
            .WithMany(s => s.Connections)
            .HasForeignKey(c => c.ChannelId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(c => c.SyncJobs)
            .WithOne(s => s.Connection)
            .HasForeignKey(s => s.ConnectionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
