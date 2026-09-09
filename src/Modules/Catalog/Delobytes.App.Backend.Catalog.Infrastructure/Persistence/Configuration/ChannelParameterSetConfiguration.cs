using Delobytes.App.Backend.Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Persistence.Configurations;

public class ChannelParameterSetConfiguration : IEntityTypeConfiguration<ChannelParameterSet>
{
    public void Configure(EntityTypeBuilder<ChannelParameterSet> builder)
    {
        builder.ToTable("ChannelParameterSets");

        builder.HasKey(cps => cps.Id);

        builder.Property(cps => cps.ChannelId)
            .IsRequired();

        builder.Property(cps => cps.CommissionPercent)
            .HasPrecision(8, 6)
            .IsRequired();

        builder.Property(cps => cps.AcquiringPercent)
            .HasPrecision(8, 6)
            .IsRequired();

        builder.Property(cps => cps.SppPercent)
            .HasPrecision(8, 6)
            .IsRequired();

        builder.Property(cps => cps.SppEnabled)
            .IsRequired();

        builder.Property(cps => cps.TaxType)
            .IsRequired();

        builder.Property(cps => cps.TaxRatePercent)
            .HasPrecision(8, 6)
            .IsRequired();

        builder.Property(cps => cps.ValidFrom)
            .IsRequired();

        builder.Property(cps => cps.CreatedAt)
            .IsRequired();

        builder.HasIndex(cps => new { cps.ChannelId, cps.ValidFrom });

        builder.HasOne(cps => cps.Channel)
            .WithMany()
            .HasForeignKey(cps => cps.ChannelId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(cps => cps.ProductChannelInputs)
            .WithOne(pci => pci.ChannelParameterSet)
            .HasForeignKey(pci => pci.ChannelParameterSetId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
