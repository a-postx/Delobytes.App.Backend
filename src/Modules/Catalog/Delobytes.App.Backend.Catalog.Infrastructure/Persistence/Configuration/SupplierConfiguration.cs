using Delobytes.App.Backend.Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Persistence.Configurations;

public class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> builder)
    {
        builder.ToTable("Suppliers");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Inn)
            .IsRequired()
            .HasMaxLength(12);

        builder.Property(s => s.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(s => s.Description)
            .HasMaxLength(1000);

        builder.Property(s => s.Phone)
            .HasMaxLength(50);

        builder.Property(s => s.Email)
            .HasMaxLength(200);

        builder.Property(s => s.IsActive)
            .IsRequired();

        builder.Property(s => s.CreatedAt)
            .IsRequired();

        builder.Property(s => s.UpdatedAt);

        builder.HasIndex(s => s.IsActive);

        // Unique per tenant
        ////builder.HasIndex("TenantId", nameof(Supplier.Inn))
        ////    .IsUnique()
        ////    .HasDatabaseName("IX_Suppliers_TenantId_Inn");

        builder.HasMany(s => s.PackagingComponents)
            .WithOne(pc => pc.Supplier)
            .HasForeignKey(pc => pc.SupplierId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
