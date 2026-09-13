using Delobytes.App.Backend.Contracts.Interfaces;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Persistence;

public sealed class TenantIdShadowPropertyConvention : IEntityTypeAddedConvention
{
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
