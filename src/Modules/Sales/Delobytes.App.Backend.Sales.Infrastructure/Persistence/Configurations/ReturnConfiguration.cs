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

        builder.Property(r => r.OrderId)
            .IsRequired();

        builder.Property(r => r.Quantity)
            .IsRequired();

        builder.Property(r => r.ReturnDate)
            .IsRequired();

        builder.Property(r => r.Reason)
            .HasMaxLength(500);

        builder.Property(r => r.RefundAmount)
            .IsRequired()
            .HasPrecision(18, 2);

        builder.Property(r => r.CreatedAt)
            .IsRequired();

        builder.HasIndex(r => r.OrderId);
        builder.HasIndex(r => r.ReturnDate);

        builder.HasOne(r => r.Order)
            .WithMany(o => o.Returns)
            .HasForeignKey(r => r.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
