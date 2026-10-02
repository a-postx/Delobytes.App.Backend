using Delobytes.App.Backend.Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core entity type configuration for Channel.
/// </summary>
public class ChannelConfiguration : IEntityTypeConfiguration<Channel>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<Channel> builder)
    {
        builder.ToTable("Channels");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.SystemChannelTemplateId);

        builder.Property(c => c.Name)
            .IsRequired()
            .HasMaxLength(200);

        // Code is the system channel template code ("wildberries", "ozon", "yandex.kit"), or
        // null for a custom channel. Duplicates SystemChannelTemplate.Code but is written at
        // channel creation time so Catalog can read it without depending on Integrations.
        builder.Property(c => c.Code)
            .HasMaxLength(50)
            .IsRequired(false);

        builder.Property(c => c.CustomApiUrl)
            .HasMaxLength(500);

        builder.Property(c => c.IsCustom)
            .IsRequired();

        builder.Property(c => c.IsActive)
            .IsRequired();

        builder.Property(c => c.CreatedAt)
            .IsRequired();

        builder.Property(c => c.UpdatedAt);

        builder.HasIndex(c => c.SystemChannelTemplateId);
        builder.HasIndex(c => c.IsActive);
        builder.HasIndex(c => c.IsCustom);
        builder.HasIndex(c => c.Code);

        // SystemChannelTemplate lives in Integrations — no FK navigation, only the ID is stored.
        builder.HasMany(c => c.ChannelProducts)
            .WithOne(cp => cp.Channel)
            .HasForeignKey(cp => cp.ChannelId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
