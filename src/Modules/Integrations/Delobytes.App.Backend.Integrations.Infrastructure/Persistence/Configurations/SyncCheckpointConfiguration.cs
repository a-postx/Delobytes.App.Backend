using Delobytes.App.Backend.Integrations.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core entity type configuration for SyncCheckpoint.
/// </summary>
public class SyncCheckpointConfiguration : IEntityTypeConfiguration<SyncCheckpoint>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<SyncCheckpoint> builder)
    {
        builder.ToTable("SyncCheckpoints");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.ConnectionId)
            .IsRequired();

        builder.Property(s => s.JobType)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(s => s.LastChangedAt);

        builder.Property(s => s.LastSuccessAt)
            .IsRequired();

        builder.Property(s => s.LastJobRunId);

        // One watermark per connection/job-type combination.
        builder.HasIndex(s => new { s.ConnectionId, s.JobType })
            .IsUnique()
            .HasDatabaseName("IX_SyncCheckpoints_ConnectionId_JobType");

        builder.HasOne(s => s.Connection)
            .WithMany()
            .HasForeignKey(s => s.ConnectionId)
            .OnDelete(DeleteBehavior.Restrict);

        // SetNull: losing the audit trail of which run last touched the checkpoint must not
        // delete the watermark itself (JobRun history can be purged independently).
        builder.HasOne(s => s.LastJobRun)
            .WithMany()
            .HasForeignKey(s => s.LastJobRunId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
