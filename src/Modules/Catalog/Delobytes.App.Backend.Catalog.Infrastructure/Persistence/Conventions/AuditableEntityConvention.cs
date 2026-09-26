using Delobytes.App.Backend.Contracts.Interfaces;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Persistence.Conventions;

/// <summary>
/// EF Core convention that configures audit properties for entities implementing
/// <see cref="IAuditableEntity"/>. Timestamps and user identifiers are required
/// to be IsRequired or nullable in the schema; actual values are populated at
/// save time by <see cref="Interceptors.AuditableEntityInterceptor"/>.
/// </summary>
public sealed class AuditableEntityConvention : IEntityTypeAddedConvention
{
    public void ProcessEntityTypeAdded(
        IConventionEntityTypeBuilder entityTypeBuilder,
        IConventionContext<IConventionEntityTypeBuilder> context)
    {
        Type clrType = entityTypeBuilder.Metadata.ClrType;

        if (clrType is null || !typeof(IAuditableEntity).IsAssignableFrom(clrType))
        {
            return;
        }

        entityTypeBuilder.Property(typeof(DateTimeOffset), nameof(IAuditableEntity.CreatedAt))
            ?.IsRequired(true, fromDataAnnotation: false);

        entityTypeBuilder.Property(typeof(DateTimeOffset?), nameof(IAuditableEntity.UpdatedAt))
            ?.IsRequired(false, fromDataAnnotation: false);

        // Nullable by design: system-initiated operations (seeders, background jobs,
        // message consumers without a user) do not have a user context.
        entityTypeBuilder.Property(typeof(Guid?), nameof(IAuditableEntity.CreatedByUserId))
            ?.IsRequired(false, fromDataAnnotation: false);

        entityTypeBuilder.Property(typeof(Guid?), nameof(IAuditableEntity.UpdatedByUserId))
            ?.IsRequired(false, fromDataAnnotation: false);
    }
}
