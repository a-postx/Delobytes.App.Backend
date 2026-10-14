using Delobytes.App.Backend.Integrations.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core entity type configuration for OrdersImportJob.
/// </summary>
public class OrdersImportJobConfiguration : IEntityTypeConfiguration<OrdersImportJob>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<OrdersImportJob> builder)
    {
        builder.ToTable("OrdersImportJobs");

        builder.HasKey(o => o.JobRunId);

        builder.HasOne(o => o.JobRun)
            .WithOne()
            .HasForeignKey<OrdersImportJob>(o => o.JobRunId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
