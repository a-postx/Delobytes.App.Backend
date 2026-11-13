using Delobytes.App.Backend.Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core entity type configuration for NumberingScheme.
/// </summary>
public class NumberingSchemeConfiguration : IEntityTypeConfiguration<NumberingScheme>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<NumberingScheme> builder)
    {
        builder.ToTable("NumberingSchemes");

        builder.HasKey(scheme => scheme.Id);

        builder.Property(scheme => scheme.Code)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(scheme => scheme.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(scheme => scheme.EntityKind)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(scheme => scheme.IsDefault)
            .IsRequired();

        builder.Property(scheme => scheme.IsActive)
            .IsRequired();

        builder.Property(scheme => scheme.CreatedAt)
            .IsRequired();

        builder.Property(scheme => scheme.UpdatedAt);

        builder.HasIndex("TenantId", nameof(NumberingScheme.Code))
            .IsUnique()
            .HasDatabaseName("IX_NumberingSchemes_TenantId_Code");

        // Filtered on IsDefault only: an inactive default still occupies the slot, so choosing a
        // replacement is an explicit administrative switch, never an implicit second default.
        builder.HasIndex("TenantId", nameof(NumberingScheme.EntityKind))
            .IsUnique()
            .HasFilter("\"IsDefault\"")
            .HasDatabaseName("IX_NumberingSchemes_TenantId_Kind_Default");

        builder.HasMany(scheme => scheme.Segments)
            .WithOne(segment => segment.Scheme)
            .HasForeignKey(segment => segment.SchemeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
