using Delobytes.App.Backend.Identity.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Delobytes.App.Backend.Identity.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core entity type configuration for Invitation.
/// </summary>
public class InvitationConfiguration : IEntityTypeConfiguration<Invitation>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<Invitation> builder)
    {
        builder.ToTable("Invitations");

        builder.HasKey(i => i.Id);

        builder.Property(i => i.TenantId)
            .IsRequired();

        builder.Property(i => i.Email)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(i => i.Role)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(i => i.Token)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(i => i.CreatedAt)
            .IsRequired();

        builder.Property(i => i.ExpiresAt)
            .IsRequired();

        builder.Property(i => i.IsAccepted)
            .IsRequired()
            .HasDefaultValue(false);

        builder.HasIndex(i => i.Token)
            .IsUnique();

        builder.HasIndex(i => new { i.TenantId, i.Email });

        builder.HasOne(i => i.Tenant)
            .WithMany()
            .HasForeignKey(i => i.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
