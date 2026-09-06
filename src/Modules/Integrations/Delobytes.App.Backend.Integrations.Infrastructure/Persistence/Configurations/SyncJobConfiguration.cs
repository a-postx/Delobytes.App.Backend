using Delobytes.App.Backend.Integrations.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core entity type configuration for SyncJob.
/// </summary>
public class SyncJobConfiguration : IEntityTypeConfiguration<SyncJob>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<SyncJob> builder)
    {
        builder.ToTable("SyncJobs");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.ConnectionId)
            .IsRequired();

        builder.Property(s => s.JobType)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(s => s.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(s => s.DateRangeFrom)
            .IsRequired();

        builder.Property(s => s.DateRangeTo)
            .IsRequired();

        builder.Property(s => s.StartedAt);

        builder.Property(s => s.CompletedAt);

        builder.Property(s => s.ErrorMessage)
            .HasMaxLength(2000);

        builder.Property(s => s.RecordsProcessed)
            .IsRequired();

        builder.Property(s => s.RecordsImported)
            .IsRequired();

        builder.HasIndex(s => s.ConnectionId);
        builder.HasIndex(s => s.Status);
        builder.HasIndex(s => s.JobType);
        builder.HasIndex(s => s.DateRangeFrom);
        builder.HasIndex(s => s.StartedAt);

        builder.HasOne(s => s.Connection)
            .WithMany(c => c.SyncJobs)
            .HasForeignKey(s => s.ConnectionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(s => s.RawApiResponses)
            .WithOne(r => r.SyncJob)
            .HasForeignKey(r => r.SyncJobId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
