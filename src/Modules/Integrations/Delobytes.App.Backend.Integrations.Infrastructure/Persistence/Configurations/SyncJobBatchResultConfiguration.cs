using Delobytes.App.Backend.Integrations.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core entity type configuration for SyncJobBatchResult.
/// </summary>
public class SyncJobBatchResultConfiguration : IEntityTypeConfiguration<SyncJobBatchResult>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<SyncJobBatchResult> builder)
    {
        builder.ToTable("SyncJobBatchResults");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.SyncJobId)
            .IsRequired();

        // Deduplication key: one row per MassTransit MessageId.
        builder.Property(r => r.MessageId)
            .IsRequired();

        builder.HasIndex(r => r.MessageId)
            .IsUnique();

        builder.HasIndex(r => r.SyncJobId);

        builder.Property(r => r.RecordsProcessed)
            .IsRequired();

        builder.Property(r => r.RecordsCreated)
            .IsRequired();

        builder.Property(r => r.RecordsUpdated)
            .IsRequired();

        builder.Property(r => r.RecordsSkipped)
            .IsRequired();

        builder.Property(r => r.RecordsFailed)
            .IsRequired();

        builder.Property(r => r.ErrorMessage)
            .HasMaxLength(2000);

        builder.Property(r => r.IsLastBatch)
            .IsRequired();

        builder.Property(r => r.ReceivedAt)
            .IsRequired();

        builder.HasOne(r => r.SyncJob)
            .WithMany(s => s.BatchResults)
            .HasForeignKey(r => r.SyncJobId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
