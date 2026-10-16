using Delobytes.App.Backend.Integrations.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core entity type configuration for JobSchedule.
/// </summary>
public class JobScheduleConfiguration : IEntityTypeConfiguration<JobSchedule>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<JobSchedule> builder)
    {
        builder.ToTable("JobSchedules");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.JobType)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(s => s.CronExpression)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(s => s.IsEnabled)
            .IsRequired();

        builder.Property(s => s.LastRunAt);

        builder.Property(s => s.NextRunAt);

        // One schedule per (ConnectionId, JobType); also the natural lookup key for upserts.
        builder.HasIndex(s => new { s.ConnectionId, s.JobType })
            .IsUnique()
            .HasDatabaseName("IX_JobSchedules_ConnectionId_JobType");

        // Scheduler tick query: enabled schedules due to run, across all tenants.
        builder.HasIndex(s => new { s.IsEnabled, s.NextRunAt })
            .HasDatabaseName("IX_JobSchedules_IsEnabled_NextRunAt");

        builder.HasOne(s => s.Connection)
            .WithMany()
            .HasForeignKey(s => s.ConnectionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
