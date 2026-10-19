using Delobytes.App.Backend.Integrations.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core entity type configuration for WildberriesMethodThrottle. Not tenant-scoped by design:
/// the rate limit is enforced by Wildberries against the raw API key, which may be shared across
/// tenants, so this table is deliberately excluded from the tenant query filter applied in
/// <see cref="IntegrationsDbContext"/> (it is not marked <c>ITenantScoped</c>).
/// </summary>
public class WildberriesMethodThrottleConfiguration : IEntityTypeConfiguration<WildberriesMethodThrottle>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<WildberriesMethodThrottle> builder)
    {
        builder.ToTable("WildberriesMethodThrottles");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.KeyFingerprint)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(t => t.Method)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(t => t.NextRequestAllowedAt)
            .IsRequired();

        builder.Property(t => t.CurrentIntervalSeconds)
            .IsRequired();

        builder.Property(t => t.LastLimit);

        builder.Property(t => t.LastRemaining);

        builder.Property(t => t.LastRetryAfterSeconds);

        builder.Property(t => t.LastStatusCode);

        builder.Property(t => t.ObservedWindowSeconds);

        builder.Property(t => t.SuccessStreak)
            .IsRequired();

        builder.Property(t => t.LastRequestAt)
            .IsRequired();

        // The limit belongs to the key, not to a connection: one pacing row per (key, method).
        builder.HasIndex(t => new { t.KeyFingerprint, t.Method })
            .IsUnique()
            .HasDatabaseName("IX_WildberriesMethodThrottles_KeyFingerprint_Method");
    }
}
