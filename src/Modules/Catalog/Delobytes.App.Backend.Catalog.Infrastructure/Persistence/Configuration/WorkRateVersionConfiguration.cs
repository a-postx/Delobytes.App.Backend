using Delobytes.App.Backend.Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Persistence.Configurations;

public class WorkRateVersionConfiguration : IEntityTypeConfiguration<WorkRateVersion>
{
    public void Configure(EntityTypeBuilder<WorkRateVersion> builder)
    {
        builder.ToTable("WorkRateVersions");

        builder.HasKey(v => v.Id);

        builder.Property(v => v.WorkRateId)
            .IsRequired();

        builder.Property(v => v.DailyWage)
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(v => v.ValidFrom)
            .IsRequired();

        builder.Property(v => v.IsActive)
            .IsRequired();

        builder.Property(v => v.CreatedAt)
            .IsRequired();

        builder.Property(v => v.UpdatedAt);

        // Composite index over tenant, work rate and effective date (shadow TenantId, as in ComponentPrices
        // the leading tenant column). Explicit name keeps the identifier inside PostgreSQL's 63-character limit.
        builder.HasIndex("TenantId", nameof(WorkRateVersion.WorkRateId), nameof(WorkRateVersion.ValidFrom))
            .HasDatabaseName("IX_WorkRateVersions_TenantId_WorkRateId_ValidFrom");

        builder.HasIndex(v => v.IsActive)
            .HasDatabaseName("IX_WorkRateVersions_IsActive");

        builder.HasOne(v => v.WorkRate)
            .WithMany(wr => wr.Versions)
            .HasForeignKey(v => v.WorkRateId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
