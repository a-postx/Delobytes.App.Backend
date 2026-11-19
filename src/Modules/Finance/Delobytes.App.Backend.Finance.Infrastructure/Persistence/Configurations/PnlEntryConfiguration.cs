using Delobytes.App.Backend.Finance.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Delobytes.App.Backend.Finance.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core entity type configuration for <see cref="PnlEntry"/>.
/// </summary>
public class PnlEntryConfiguration : IEntityTypeConfiguration<PnlEntry>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<PnlEntry> builder)
    {
        builder.ToTable(
            "PnlEntries",
            table =>
            {
                // The sign lives in Direction; a negative amount means the writer did not normalise
                // it, and the aggregate refuses to guess.
                table.HasCheckConstraint("CK_PnlEntries_AmountNonNegative", "\"Amount\" >= 0");

                // An entry must be attributable: either to a unit (Wildberries srid, Ozon posting)
                // or, for an Ozon ITEM accrual, to an order plus a product.
                table.HasCheckConstraint(
                    "CK_PnlEntries_LineOrOrder",
                    "\"ExternalLineId\" IS NOT NULL OR \"ExternalOrderId\" IS NOT NULL");
            });

        builder.HasKey(entry => entry.Id);

        builder.Property(entry => entry.Source)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(entry => entry.ReportInstanceKey)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(entry => entry.ExternalEntryId)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(entry => entry.ChannelId)
            .IsRequired();

        builder.Property(entry => entry.ExternalOrderId)
            .HasMaxLength(100);

        builder.Property(entry => entry.ExternalLineId)
            .HasMaxLength(100);

        builder.Property(entry => entry.ExternalProductId)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(entry => entry.Kind)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(entry => entry.Type)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        // Six decimals, not two: Wildberries reports amounts such as 7.041 and
        // -21.0991803278688525, and rounding on the way in would make the components stop adding up
        // to the payout. Rounding belongs to presentation only.
        builder.Property(entry => entry.Amount)
            .HasPrecision(18, 6);

        builder.Property(entry => entry.Direction)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(entry => entry.Currency)
            .IsRequired()
            .HasMaxLength(3);

        builder.Property(entry => entry.DocumentDate)
            .IsRequired();

        builder.Property(entry => entry.PeriodFrom)
            .IsRequired();

        builder.Property(entry => entry.PeriodTo)
            .IsRequired();

        builder.Property(entry => entry.ImportedAt)
            .IsRequired();

        builder.Property(entry => entry.SupersededByEntryId);
        builder.Property(entry => entry.SupersededAt);

        builder.Property(entry => entry.CreatedAt)
            .IsRequired();

        // The idempotency key. One report row can carry several typed amounts, so the type is part
        // of the identity; a re-issued report is a different instance and therefore a different key,
        // which is what leaves room for a correction to be appended and the old row superseded.
        // The channel is part of the key because the channel-side identities are unique per seller
        // account: two shops of one marketplace in the same tenant re-use the same report and accrual
        // numbers, and without the channel the second shop's rows would be dropped as duplicates.
        builder.HasIndex(
                "TenantId",
                nameof(PnlEntry.ChannelId),
                nameof(PnlEntry.Source),
                nameof(PnlEntry.ReportInstanceKey),
                nameof(PnlEntry.ExternalEntryId),
                nameof(PnlEntry.ExternalProductId),
                nameof(PnlEntry.Kind),
                nameof(PnlEntry.Type))
            .IsUnique();

        // Aggregation by unit, by order and by product. Filtered on the current version: the
        // aggregate reads only rows that no newer report instance has replaced.
        builder.HasIndex("TenantId", nameof(PnlEntry.ChannelId), nameof(PnlEntry.ExternalLineId))
            .HasFilter("\"SupersededByEntryId\" IS NULL");

        builder.HasIndex(
                "TenantId",
                nameof(PnlEntry.ChannelId),
                nameof(PnlEntry.ExternalOrderId),
                nameof(PnlEntry.ExternalProductId))
            .HasFilter("\"SupersededByEntryId\" IS NULL");

        builder.HasIndex("TenantId", nameof(PnlEntry.DocumentDate));
    }
}
