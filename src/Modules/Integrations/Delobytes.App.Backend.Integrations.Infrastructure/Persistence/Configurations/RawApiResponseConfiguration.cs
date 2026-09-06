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

        builder.Property(r => r.SyncJobId)
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

        builder.Property(r => r.ProcessedAt);

        builder.HasIndex(r => r.SyncJobId);
        builder.HasIndex(r => r.ReceivedAt);
        builder.HasIndex(r => r.ProcessedAt);
        builder.HasIndex(r => r.HttpStatusCode);

        builder.HasOne(r => r.SyncJob)
            .WithMany(s => s.RawApiResponses)
            .HasForeignKey(r => r.SyncJobId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
