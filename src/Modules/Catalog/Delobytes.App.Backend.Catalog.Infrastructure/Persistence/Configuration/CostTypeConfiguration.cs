using Delobytes.App.Backend.Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Persistence.Configurations;

public class CostTypeConfiguration : IEntityTypeConfiguration<CostType>
{
    public void Configure(EntityTypeBuilder<CostType> builder)
    {
        builder.ToTable("CostTypes");

        builder.HasKey(ct => ct.Id);

        builder.Property(ct => ct.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(ct => ct.Description)
            .HasMaxLength(1000);

        builder.Property(ct => ct.IsActive)
            .IsRequired();

        builder.Property(ct => ct.CreatedAt)
            .IsRequired();

        builder.HasIndex(ct => ct.IsActive);

        builder.HasMany(ct => ct.ProductChannelCosts)
            .WithOne(pcc => pcc.CostType)
            .HasForeignKey(pcc => pcc.CostTypeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
