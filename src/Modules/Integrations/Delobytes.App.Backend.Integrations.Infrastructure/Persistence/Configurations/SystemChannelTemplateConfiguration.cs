using Delobytes.App.Backend.Integrations.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core entity type configuration for SystemChannelTemplate.
/// </summary>
public class SystemChannelTemplateConfiguration : IEntityTypeConfiguration<SystemChannelTemplate>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<SystemChannelTemplate> builder)
    {
        builder.ToTable("SystemChannelTemplates");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Code)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(s => s.DisplayName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(s => s.ApiBaseUrl)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(s => s.ApiVersion)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(s => s.Description)
            .HasMaxLength(1000);

        builder.Property(s => s.IsActive)
            .IsRequired();

        builder.Property(s => s.CreatedAt)
            .IsRequired();

        builder.HasIndex(s => s.Code)
            .IsUnique();

        builder.HasIndex(s => s.IsActive);

        builder.HasMany(s => s.Connections)
            .WithOne(c => c.Channel)
            .HasForeignKey(c => c.ChannelId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
