using Delobytes.App.Backend.Integrations.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core entity type configuration for JobRun.
/// </summary>
public class JobRunConfiguration : IEntityTypeConfiguration<JobRun>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<JobRun> builder)
    {
        builder.ToTable("JobRuns");

        builder.HasKey(j => j.Id);

        builder.Property(j => j.ConnectionId)
            .IsRequired();

        builder.Property(j => j.JobType)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(j => j.Status)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(j => j.RequestedByUserId);

        builder.Property(j => j.CreatedAt)
            .IsRequired();

        builder.Property(j => j.StartedAt);

        builder.Property(j => j.CompletedAt);

        builder.Property(j => j.Error)
            .HasMaxLength(2000);

        builder.Property(j => j.RecordsCreated)
            .IsRequired();

        builder.Property(j => j.RecordsUpdated)
            .IsRequired();

        builder.Property(j => j.RecordsSkipped)
            .IsRequired();

        builder.Property(j => j.RecordsFailed)
            .IsRequired();

        builder.Property(j => j.NotBefore);

        // Worker query: pick Pending jobs for a tenant within its parallelism limit.
        builder.HasIndex(j => new { j.ConnectionId, j.JobType, j.Status })
            .HasDatabaseName("IX_JobRuns_ConnectionId_JobType_Status");

        // Watchdog query: Running jobs older than a timeout threshold, across all tenants.
        builder.HasIndex(j => new { j.Status, j.StartedAt })
            .HasDatabaseName("IX_JobRuns_Status_StartedAt");

        // Enforces "one active job per (ConnectionId, JobType)" at the database level;
        // the HasActiveJobAsync check alone is not safe under concurrent requests.
        // Status is mapped as int: 0=Pending, 1=Running (see JobStatus enum).
        builder.HasIndex(j => new { j.ConnectionId, j.JobType })
            .IsUnique()
            .HasDatabaseName("IX_JobRuns_ActivePerConnectionType")
            .HasFilter("\"Status\" IN (0, 1)");

        builder.HasOne(j => j.Connection)
            .WithMany()
            .HasForeignKey(j => j.ConnectionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
