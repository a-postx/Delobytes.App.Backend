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

        builder.HasOne(c => c.SystemChannelTemplate)
            .WithMany()
            .HasForeignKey(c => c.SystemChannelTemplateId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(c => c.ChannelProducts)
            .WithOne(cp => cp.Channel)
            .HasForeignKey(cp => cp.ChannelId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
