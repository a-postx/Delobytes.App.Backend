using Delobytes.App.Backend.Integrations.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core entity type configuration for RawApiResponse.
/// </summary>
public class RawApiResponseConfiguration : IEntityTypeConfiguration<RawApiResponse>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<RawApiResponse> builder)
    {
        builder.ToTable("RawApiResponses");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.JobRunId)
            .IsRequired();

        builder.Property(r => r.Endpoint)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(r => r.RequestPayload)
            .IsRequired()
            .HasColumnType("text");

        builder.Property(r => r.ResponsePayload)
            .IsRequired()
            .HasColumnType("text");

        builder.Property(r => r.HttpStatusCode)
            .IsRequired();

        builder.Property(r => r.ReceivedAt)
            .IsRequired();

        builder.HasIndex(r => r.JobRunId);
        builder.HasIndex(r => r.ReceivedAt);
        builder.HasIndex(r => r.HttpStatusCode);

        // Cascade: an audit trail with no JobRun to attribute it to carries no meaning
        // (decision 54 — the raw capture dies together with the job it was recorded for).
        builder.HasOne(r => r.JobRun)
            .WithMany()
            .HasForeignKey(r => r.JobRunId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
