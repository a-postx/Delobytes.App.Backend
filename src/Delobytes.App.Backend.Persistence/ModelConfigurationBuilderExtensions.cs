using Delobytes.App.Backend.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;

namespace Delobytes.App.Backend.Persistence;

/// <summary>
/// Registration helpers for the shared EF Core model conventions.
/// </summary>
public static class ModelConfigurationBuilderExtensions
{
    /// <summary>
    /// Registers the shared model conventions (tenant shadow property, audit properties and
    /// RowVersion/xmin mapping) for a module DbContext. Call from
    /// <c>DbContext.ConfigureConventions</c>.
    /// </summary>
    /// <param name="configurationBuilder">Model configuration builder of the DbContext.</param>
    /// <returns>The same <see cref="ModelConfigurationBuilder"/> for chaining.</returns>
    public static ModelConfigurationBuilder AddSharedModelConventions(
        this ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Conventions.Add(_ => new TenantIdShadowPropertyConvention());
        configurationBuilder.Conventions.Add(_ => new AuditableEntityConvention());
        configurationBuilder.Conventions.Add(_ => new RowVersionedEntityConvention());

        return configurationBuilder;
    }
}
