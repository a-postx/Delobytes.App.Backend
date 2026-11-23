using Delobytes.App.Backend.Identity.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Delobytes.App.Backend.Identity.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core entity type configuration for User.
/// </summary>
public class UserConfiguration : IEntityTypeConfiguration<User>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");

        builder.HasKey(u => u.Id);

        builder.Property(u => u.ExternalId)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(u => u.IdentityProvider)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(u => u.Email)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(u => u.NormalizedEmail)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(u => u.DisplayName)
            .HasMaxLength(200);

        builder.Property(u => u.PasswordHash)
            .HasMaxLength(500);

        builder.Property(u => u.CreatedAt)
            .IsRequired();

        builder.Property(u => u.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(u => u.TokensValidAfter)
            .IsRequired(false);

        builder.Property(u => u.LastActiveTenantId)
            .IsRequired(false);

        builder.HasIndex(u => new { u.ExternalId, u.IdentityProvider })
            .IsUnique();

        // Email is unique only inside one provider: a local account and an OAuth account
        // may legitimately share an address until account linking exists. Empty addresses
        // (a provider may not return one) are excluded so they never collide.
        builder.HasIndex(u => new { u.NormalizedEmail, u.IdentityProvider })
            .IsUnique()
            .HasFilter("\"NormalizedEmail\" <> ''")
            .HasDatabaseName("IX_Users_NormalizedEmail_IdentityProvider");

        builder.HasMany(u => u.Memberships)
            .WithOne(m => m.User)
            .HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
