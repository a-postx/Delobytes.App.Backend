using Delobytes.App.Backend.Integrations.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core entity type configuration for JobTypeConfiguration.
/// </summary>
public class JobTypeConfigurationConfiguration : IEntityTypeConfiguration<JobTypeConfiguration>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<JobTypeConfiguration> builder)
    {
        builder.ToTable("JobTypeConfigurations");

        builder.HasKey(c => c.JobType);

        builder.Property(c => c.JobType)
            .IsRequired()
            .HasConversion<int>()
            .ValueGeneratedNever();

        builder.Property(c => c.TimeoutMinutes)
            .IsRequired();

        builder.Property(c => c.BatchSize)
            .IsRequired();
    }
}
