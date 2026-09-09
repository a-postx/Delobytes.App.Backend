using Delobytes.App.Backend.Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Persistence.Configurations;

public class TariffGridEntryConfiguration : IEntityTypeConfiguration<TariffGridEntry>
{
    public void Configure(EntityTypeBuilder<TariffGridEntry> builder)
    {
        builder.ToTable("TariffGridEntries");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.TariffGridId)
            .IsRequired();

        builder.Property(e => e.RegionOrCity)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(e => e.VolumeThresholdLiters)
            .HasPrecision(10, 3);

        builder.Property(e => e.Rate)
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(e => e.CreatedAt)
            .IsRequired();

        builder.HasIndex(e => new { e.TariffGridId, e.RegionOrCity, e.VolumeThresholdLiters })
            .IsUnique();

        builder.HasOne(e => e.TariffGrid)
            .WithMany(tg => tg.Entries)
            .HasForeignKey(e => e.TariffGridId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
