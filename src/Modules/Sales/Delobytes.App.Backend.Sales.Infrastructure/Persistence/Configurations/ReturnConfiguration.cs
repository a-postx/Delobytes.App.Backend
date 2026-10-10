using Delobytes.App.Backend.Sales.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Delobytes.App.Backend.Sales.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core entity type configuration for Return.
/// </summary>
public class ReturnConfiguration : IEntityTypeConfiguration<Return>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<Return> builder)
    {
        builder.ToTable("Returns");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.OrderLineId)
            .IsRequired();

        builder.Property(r => r.ExternalReturnId)
            .HasMaxLength(100);

        builder.Property(r => r.Quantity)
            .IsRequired();

        builder.Property(r => r.ReturnDate)
            .IsRequired();

        builder.Property(r => r.Kind)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(r => r.Reason)
            .HasMaxLength(500);

        builder.Property(r => r.RefundAmount)
            .HasPrecision(18, 2);

        builder.Property(r => r.Currency)
            .HasMaxLength(3);

        builder.Property(r => r.RefundSource)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(r => r.RawDataId);

        builder.Property(r => r.CreatedAt)
            .IsRequired();

        builder.HasIndex("TenantId", nameof(Return.OrderLineId));
        builder.HasIndex("TenantId", nameof(Return.ReturnDate));

        builder.HasOne(r => r.OrderLine)
            .WithMany(l => l.Returns)
            .HasForeignKey(r => r.OrderLineId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
