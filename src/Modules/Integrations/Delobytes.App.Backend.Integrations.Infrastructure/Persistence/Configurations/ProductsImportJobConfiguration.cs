using Delobytes.App.Backend.Integrations.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core entity type configuration for ProductsImportJob.
/// </summary>
public class ProductsImportJobConfiguration : IEntityTypeConfiguration<ProductsImportJob>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<ProductsImportJob> builder)
    {
        builder.ToTable("ProductsImportJobs");

        builder.HasKey(p => p.JobRunId);

        builder.Property(p => p.TotalImportBatches);

        builder.HasOne(p => p.JobRun)
            .WithOne()
            .HasForeignKey<ProductsImportJob>(p => p.JobRunId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
