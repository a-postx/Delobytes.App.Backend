using Delobytes.App.Backend.Contracts.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace Delobytes.App.Backend.Persistence.Conventions;

/// <summary>
/// EF Core convention that configures RowVersion property as a PostgreSQL xmin concurrency token
/// for entities implementing <see cref="IRowVersionedEntity"/>.
/// </summary>
public sealed class RowVersionedEntityConvention : IEntityTypeAddedConvention
{
    /// <inheritdoc/>
    public void ProcessEntityTypeAdded(
        IConventionEntityTypeBuilder entityTypeBuilder,
        IConventionContext<IConventionEntityTypeBuilder> context)
    {
        Type clrType = entityTypeBuilder.Metadata.ClrType;

        if (clrType is not null && typeof(IRowVersionedEntity).IsAssignableFrom(clrType))
        {
            IConventionPropertyBuilder? rowVersion = entityTypeBuilder.Property(
                typeof(uint), nameof(IRowVersionedEntity.RowVersion));

            if (rowVersion is null)
            {
                return;
            }

            // Separate statements on purpose: every fluent call returns a nullable builder, so a
            // chain would dereference a possible null instead of mutating the builder in place.
            rowVersion.IsRequired(true, fromDataAnnotation: false);
            rowVersion.IsConcurrencyToken(true, fromDataAnnotation: false);
            rowVersion.ValueGenerated(ValueGenerated.OnAddOrUpdate, fromDataAnnotation: false);
            rowVersion.HasColumnName("xmin");
            rowVersion.HasColumnType("xid");
        }
    }
}
