using Delobytes.App.Backend.Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Persistence.Configurations;

public class ProductWorkRateConfiguration : IEntityTypeConfiguration<ProductWorkRate>
{
    public void Configure(EntityTypeBuilder<ProductWorkRate> builder)
    {
        builder.ToTable("ProductWorkRates");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.ProductId)
            .IsRequired();

        builder.Property(r => r.WorkRateId)
            .IsRequired();

        builder.Property(r => r.AssemblyRatePerDay)
            .IsRequired();

        builder.Property(r => r.ValidFrom)
            .IsRequired();

        builder.Property(r => r.Status)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(r => r.RemovedFrom)
            .IsRequired(false);

        builder.Property(r => r.CreatedAt)
            .IsRequired();

        builder.Property(r => r.UpdatedAt);

        // Unique and deliberately unfiltered on RemovedFrom: a removed version and a new one sharing
        // a ValidFrom would both satisfy the effective-date query for a date before the removal, and
        // the CreatedAt tie-break would then silently cancel the removal. One date, one version.
        // The index name is written out because the conflict translator matches on it, and because
        // PostgreSQL truncates identifiers at 63 characters.
        builder.HasIndex("TenantId", nameof(ProductWorkRate.ProductId), nameof(ProductWorkRate.ValidFrom))
            .IsUnique()
            .HasDatabaseName("IX_ProductWorkRates_TenantId_ProductId_ValidFrom");

        builder.HasIndex(r => r.WorkRateId);

        builder.HasIndex(r => r.Status)
            .HasDatabaseName("IX_ProductWorkRates_Status");

        builder.HasOne(r => r.Product)
            .WithMany(p => p.ProductWorkRates)
            .HasForeignKey(r => r.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(r => r.WorkRate)
            .WithMany(wr => wr.ProductWorkRates)
            .HasForeignKey(r => r.WorkRateId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
