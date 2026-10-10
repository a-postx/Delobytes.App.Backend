using Delobytes.App.Backend.Contracts.Interfaces;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;

namespace Delobytes.App.Backend.Persistence.Conventions;

/// <summary>
/// Declares the TenantId shadow property on every <see cref="ITenantScoped"/> entity as soon as
/// the entity type is added to the model — before any <see cref="IEntityTypeConfiguration{TEntity}"/>
/// runs. Entity configurations reference "TenantId" by name in composite <c>HasIndex</c> calls;
/// without this convention EF cannot infer the shadow property's CLR type from a bare string
/// reference and throws <see cref="InvalidOperationException"/> ("no property type was specified").
/// </summary>
/// <remarks>
/// The property CLR type is <see cref="Guid"/> rather than <see cref="Nullable{Guid}"/> with
/// <c>IsRequired</c>: both produce the same <c>uuid NOT NULL</c> column, but switching an existing
/// module to the nullable form would drift its model snapshot and emit a spurious migration.
/// </remarks>
public sealed class TenantIdShadowPropertyConvention : IEntityTypeAddedConvention
{
    /// <inheritdoc/>
    public void ProcessEntityTypeAdded(
        IConventionEntityTypeBuilder entityTypeBuilder,
        IConventionContext<IConventionEntityTypeBuilder> context)
    {
        Type clrType = entityTypeBuilder.Metadata.ClrType;

        if (clrType is not null && typeof(ITenantScoped).IsAssignableFrom(clrType))
        {
            entityTypeBuilder.Property(typeof(Guid), "TenantId", fromDataAnnotation: false)
                ?.IsRequired(true, fromDataAnnotation: false);
        }
    }
}
