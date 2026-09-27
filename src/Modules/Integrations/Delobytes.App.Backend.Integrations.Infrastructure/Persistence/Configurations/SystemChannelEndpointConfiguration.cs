using Delobytes.App.Backend.Integrations.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core entity type configuration for SystemChannelEndpoint.
/// </summary>
public class SystemChannelEndpointConfiguration : IEntityTypeConfiguration<SystemChannelEndpoint>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<SystemChannelEndpoint> builder)
    {
        builder.ToTable("SystemChannelEndpoints");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.SystemChannelTemplateId)
            .IsRequired();

        builder.Property(e => e.EndpointType)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(e => e.BaseUrl)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(e => e.Description)
            .HasMaxLength(1000);

        builder.Property(e => e.IsActive)
            .IsRequired();

        builder.Property(e => e.CreatedAt)
            .IsRequired();

        builder.HasIndex(e => new { e.SystemChannelTemplateId, e.EndpointType })
            .IsUnique();

        builder.HasIndex(e => e.IsActive);

        builder.HasOne(e => e.SystemChannelTemplate)
            .WithMany(t => t.Endpoints)
            .HasForeignKey(e => e.SystemChannelTemplateId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
