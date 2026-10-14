using Delobytes.App.Backend.Integrations.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core entity type configuration for JobCheckpoint.
/// </summary>
public class JobCheckpointConfiguration : IEntityTypeConfiguration<JobCheckpoint>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<JobCheckpoint> builder)
    {
        builder.ToTable("JobCheckpoints");

        builder.HasKey(c => c.JobRunId);

        builder.Property(c => c.Payload)
            .IsRequired()
            .HasMaxLength(4000);

        builder.Property(c => c.UpdatedAt)
            .IsRequired();

        builder.HasOne(c => c.JobRun)
            .WithOne()
            .HasForeignKey<JobCheckpoint>(c => c.JobRunId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
