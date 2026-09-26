using Delobytes.App.Backend.Contracts.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Persistence.Conventions;

/// <summary>
/// EF Core convention that configures RowVersion property as a PostgreSQL xmin concurrency token
/// for entities implementing <see cref="IRowVersionedEntity"/>.
/// </summary>
public sealed class RowVersionedEntityConvention : IEntityTypeAddedConvention
{
    public void ProcessEntityTypeAdded(
        IConventionEntityTypeBuilder entityTypeBuilder,
        IConventionContext<IConventionEntityTypeBuilder> context)
    {
        Type clrType = entityTypeBuilder.Metadata.ClrType;

        if (clrType is not null && typeof(IRowVersionedEntity).IsAssignableFrom(clrType))
        {
            entityTypeBuilder.Property(typeof(uint), nameof(IRowVersionedEntity.RowVersion))
                ?.IsRequired(true, fromDataAnnotation: false)
                .IsConcurrencyToken(true, fromDataAnnotation: false)
                .ValueGenerated(ValueGenerated.OnAddOrUpdate, fromDataAnnotation: false)
                .HasColumnName("xmin")
                .HasColumnType("xid");
        }
    }
}
