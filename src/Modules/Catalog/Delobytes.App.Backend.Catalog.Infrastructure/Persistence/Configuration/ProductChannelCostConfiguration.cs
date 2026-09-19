using Delobytes.App.Backend.Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Persistence.Configurations;

public class ProductChannelCostConfiguration : IEntityTypeConfiguration<ProductChannelCost>
{
    public void Configure(EntityTypeBuilder<ProductChannelCost> builder)
    {
        builder.ToTable("ProductChannelCosts");

        builder.HasKey(pcc => pcc.Id);

        builder.Property(pcc => pcc.ProductId)
            .IsRequired();

        builder.Property(pcc => pcc.ChannelId)
            .IsRequired();

        builder.Property(pcc => pcc.CostTypeId)
            .IsRequired();

        builder.Property(pcc => pcc.Amount)
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(pcc => pcc.CreatedAt)
            .IsRequired();

        builder.HasIndex(pcc => new { pcc.ProductId, pcc.ChannelId });
        builder.HasIndex(pcc => new { pcc.ProductId, pcc.ChannelId, pcc.CostTypeId })
            .IsUnique();

        builder.HasOne(pcc => pcc.Product)
            .WithMany()
            .HasForeignKey(pcc => pcc.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(pcc => pcc.Channel)
            .WithMany()
            .HasForeignKey(pcc => pcc.ChannelId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(pcc => pcc.CostType)
            .WithMany(ct => ct.ProductChannelCosts)
            .HasForeignKey(pcc => pcc.CostTypeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
