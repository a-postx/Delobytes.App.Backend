using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core entity type configuration for NumberingSegment.
/// </summary>
public class NumberingSegmentConfiguration : IEntityTypeConfiguration<NumberingSegment>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<NumberingSegment> builder)
    {
        int constant = (int)NumberingSegmentType.Constant;
        int autoNumber = (int)NumberingSegmentType.AutoNumber;

        builder.ToTable("NumberingSegments", table =>
        {
            table.HasCheckConstraint("CK_NumberingSegments_Order", "\"Order\" > 0");
            table.HasCheckConstraint(
                "CK_NumberingSegments_Constant",
                $"(\"Type\" = {constant} AND \"ConstantValue\" IS NOT NULL) OR \"Type\" <> {constant}");
            table.HasCheckConstraint(
                "CK_NumberingSegments_AutoNumber",
                $"(\"Type\" = {autoNumber} AND \"NumberingSequenceId\" IS NOT NULL) OR \"Type\" <> {autoNumber}");
        });

        builder.HasKey(segment => segment.Id);

        builder.Property(segment => segment.SchemeId)
            .IsRequired();

        builder.Property(segment => segment.Order)
            .IsRequired();

        builder.Property(segment => segment.Type)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(segment => segment.ConstantValue)
            .HasMaxLength(50);

        builder.Property(segment => segment.NumberingSequenceId);

        builder.Property(segment => segment.Length);

        builder.Property(segment => segment.SeparatorAfter)
            .HasMaxLength(5);

        builder.HasIndex(segment => new { segment.SchemeId, segment.Order })
            .IsUnique()
            .HasDatabaseName("IX_NumberingSegments_SchemeId_Order");

        // PostgreSQL does not index foreign keys automatically, and the segment-to-sequence
        // lookup runs on every allocation through the collection include.
        builder.HasIndex(segment => segment.NumberingSequenceId);

        // Restrict rather than cascade: deleting a sequence that a scheme still uses must fail
        // loudly instead of silently orphaning the segment.
        builder.HasOne(segment => segment.NumberingSequence)
            .WithMany()
            .HasForeignKey(segment => segment.NumberingSequenceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
