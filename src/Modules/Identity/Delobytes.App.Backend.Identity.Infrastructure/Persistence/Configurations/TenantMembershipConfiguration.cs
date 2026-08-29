using Delobytes.App.Backend.Identity.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Delobytes.App.Backend.Identity.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core entity type configuration for TenantMembership.
/// </summary>
public class TenantMembershipConfiguration : IEntityTypeConfiguration<TenantMembership>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<TenantMembership> builder)
    {
        builder.ToTable("TenantMemberships");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.UserId)
            .IsRequired();

        builder.Property(m => m.TenantId)
            .IsRequired();

        builder.Property(m => m.Role)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(m => m.CreatedAt)
            .IsRequired();

        builder.Property(m => m.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.HasIndex(m => new { m.UserId, m.TenantId })
            .IsUnique();

        builder.HasIndex(m => m.TenantId);
    }
}
