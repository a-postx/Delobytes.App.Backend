using Delobytes.App.Backend.Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Persistence.Configurations;

public class WorkRateConfiguration : IEntityTypeConfiguration<WorkRate>
{
    public void Configure(EntityTypeBuilder<WorkRate> builder)
    {
        builder.ToTable("WorkRates");

        builder.HasKey(wr => wr.Id);

        builder.Property(wr => wr.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(wr => wr.DailyWage)
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(wr => wr.AssemblyRatePerDay)
            .IsRequired();

        builder.Property(wr => wr.ValidFrom)
            .IsRequired();

        builder.Property(wr => wr.IsActive)
            .IsRequired();

        builder.Property(wr => wr.CreatedAt)
            .IsRequired();

        builder.Property(wr => wr.UpdatedAt);

        builder.HasIndex(wr => wr.ValidFrom);
        builder.HasIndex(wr => wr.IsActive);
    }
}
