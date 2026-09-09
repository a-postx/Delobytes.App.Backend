using Delobytes.App.Backend.Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Persistence.Configurations;

public class TariffGridConfiguration : IEntityTypeConfiguration<TariffGrid>
{
    public void Configure(EntityTypeBuilder<TariffGrid> builder)
    {
        builder.ToTable("TariffGrids");

        builder.HasKey(tg => tg.Id);

        builder.Property(tg => tg.ChannelId);

        builder.Property(tg => tg.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(tg => tg.TariffType)
            .IsRequired();

        builder.Property(tg => tg.ValidFrom)
            .IsRequired();

        builder.Property(tg => tg.IsActive)
            .IsRequired();

        builder.Property(tg => tg.CreatedAt)
            .IsRequired();

        builder.HasIndex(tg => tg.TariffType);
        builder.HasIndex(tg => tg.ValidFrom);
        builder.HasIndex(tg => tg.IsActive);

        builder.HasMany(tg => tg.Entries)
            .WithOne(e => e.TariffGrid)
            .HasForeignKey(e => e.TariffGridId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
