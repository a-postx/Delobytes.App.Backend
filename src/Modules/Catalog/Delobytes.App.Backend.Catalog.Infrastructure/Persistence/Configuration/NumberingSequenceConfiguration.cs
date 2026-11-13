using Delobytes.App.Backend.Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core entity type configuration for NumberingSequence.
/// </summary>
public class NumberingSequenceConfiguration : IEntityTypeConfiguration<NumberingSequence>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<NumberingSequence> builder)
    {
        builder.ToTable("NumberingSequences", table => table.HasCheckConstraint(
            "CK_NumberingSequences_Step",
            "\"Step\" > 0"));

        builder.HasKey(sequence => sequence.Id);

        builder.Property(sequence => sequence.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(sequence => sequence.StartNumber)
            .IsRequired()
            .HasDefaultValue(1);

        builder.Property(sequence => sequence.Step)
            .IsRequired()
            .HasDefaultValue(1);

        builder.Property(sequence => sequence.EndNumber);

        builder.Property(sequence => sequence.NextNumber)
            .IsRequired();

        builder.Property(sequence => sequence.WarnOnClose)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(sequence => sequence.WarnIfNotContinuous)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(sequence => sequence.ManualNumbering)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(sequence => sequence.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(sequence => sequence.CreatedAt)
            .IsRequired();

        builder.Property(sequence => sequence.UpdatedAt);
    }
}
