using Delobytes.App.Backend.Finance.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Delobytes.App.Backend.Finance.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core entity type configuration for <see cref="PnlLineExpectation"/>.
/// </summary>
public class PnlLineExpectationConfiguration : IEntityTypeConfiguration<PnlLineExpectation>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<PnlLineExpectation> builder)
    {
        builder.ToTable("PnlLineExpectations");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.OrderLineId)
            .IsRequired();

        builder.Property(e => e.ExpectedType)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(e => e.Source)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(e => e.ExpectedBy)
            .IsRequired();

        builder.Property(e => e.State)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(e => e.AmountEstimate)
            .HasPrecision(18, 6);

        builder.Property(e => e.Currency)
            .IsRequired()
            .HasMaxLength(3);

        builder.Property(e => e.ClosedAt);

        builder.Property(e => e.ClosedByUserId);

        builder.Property(e => e.ClosureReason)
            .HasMaxLength(200);

        builder.Property(e => e.CreatedAt)
            .IsRequired();

        builder.Property(e => e.UpdatedAt);

        // One open item per line and document type: a recompute must not create a second one, and
        // the projection upserts on this key instead of querying by hand.
        builder.HasIndex("TenantId", nameof(PnlLineExpectation.OrderLineId), nameof(PnlLineExpectation.ExpectedType))
            .IsUnique();

        // The aging pass reads exactly this: the waiting rows whose deadline has passed.
        builder.HasIndex("TenantId", nameof(PnlLineExpectation.State), nameof(PnlLineExpectation.ExpectedBy));
    }
}
