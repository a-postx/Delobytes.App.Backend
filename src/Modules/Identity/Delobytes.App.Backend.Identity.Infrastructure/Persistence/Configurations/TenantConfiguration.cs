using Delobytes.App.Backend.Identity.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Delobytes.App.Backend.Identity.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core entity type configuration for Tenant.
/// </summary>
public class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("Tenants");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(t => t.CreatedAt)
            .IsRequired();

        builder.Property(t => t.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(t => t.LegalName);
        builder.Property(t => t.LegalNameShort);
        builder.Property(t => t.Inn);
        builder.Property(t => t.Kpp);
        builder.Property(t => t.Ogrn);
        builder.Property(t => t.LegalAddress);
        builder.Property(t => t.ActualAddress);
        builder.Property(t => t.DirectorName);
        builder.Property(t => t.Phone);
        builder.Property(t => t.Email);
        builder.Property(t => t.BankAccount);
        builder.Property(t => t.BankName);
        builder.Property(t => t.Bik);
        builder.Property(t => t.CorrespondentAccount);

        builder.Property(cps => cps.TaxType);

        builder.Property(cps => cps.TaxRatePercent)
            .HasPrecision(8, 6);

        builder.Property(cps => cps.VatType);

        builder.Property(t => t.Currency);
        builder.Property(t => t.TimeZone);

        builder.HasIndex(t => t.Name);

        builder.HasMany(t => t.Memberships)
            .WithOne(m => m.Tenant)
            .HasForeignKey(m => m.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
