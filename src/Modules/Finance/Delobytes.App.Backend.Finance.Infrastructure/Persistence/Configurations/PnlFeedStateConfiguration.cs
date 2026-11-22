using Delobytes.App.Backend.Finance.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Delobytes.App.Backend.Finance.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core entity type configuration for <see cref="PnlFeedState"/>.
/// </summary>
public class PnlFeedStateConfiguration : IEntityTypeConfiguration<PnlFeedState>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<PnlFeedState> builder)
    {
        builder.ToTable("PnlFeedStates");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.ChannelId)
            .IsRequired();

        builder.Property(e => e.LastJobRunId)
            .IsRequired();

        builder.Property(e => e.LastBatchAt)
            .IsRequired();

        builder.Property(e => e.LastChangeAt);

        builder.Property(e => e.LastBatchAppended)
            .IsRequired();

        builder.Property(e => e.LastBatchDuplicates)
            .IsRequired();

        builder.Property(e => e.LastBatchFailed)
            .IsRequired();

        builder.Property(e => e.CreatedAt)
            .IsRequired();

        builder.Property(e => e.UpdatedAt);

        // One checkpoint per channel: the write is an upsert on this key, so a redelivered batch
        // updates the existing row instead of adding a second one.
        builder.HasIndex("TenantId", nameof(PnlFeedState.ChannelId))
            .IsUnique();
    }
}
