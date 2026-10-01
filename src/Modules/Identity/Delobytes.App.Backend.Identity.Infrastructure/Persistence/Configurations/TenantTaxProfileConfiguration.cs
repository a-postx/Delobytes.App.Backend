using Delobytes.App.Backend.Contracts.Accounting;
using Delobytes.App.Backend.Identity.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Delobytes.App.Backend.Identity.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core entity type configuration for TenantTaxProfile.
/// </summary>
public class TenantTaxProfileConfiguration : IEntityTypeConfiguration<TenantTaxProfile>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<TenantTaxProfile> builder)
    {
        builder.ToTable("TenantTaxProfiles");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.TenantId)
            .IsRequired();

        builder.Property(p => p.Regime)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(p => p.RatePercent)
            .IsRequired()
            .HasPrecision(5, 2);

        builder.Property(p => p.Vat)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(p => p.ValidFrom)
            .IsRequired();

        builder.Property(p => p.CreatedAt)
            .IsRequired();

        // Уникальность пары запрещает две версии профиля на одну дату: разрешать её
        // проверкой в коде нельзя — гонка двух администраторов создаст обе записи.
        builder.HasIndex(p => new { p.TenantId, p.ValidFrom })
            .IsUnique();

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(p => p.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
