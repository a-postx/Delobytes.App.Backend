using Delobytes.App.Backend.Persistence.Conventions;
using Delobytes.App.Backend.Persistence.Converters;
using Microsoft.EntityFrameworkCore;

namespace Delobytes.App.Backend.Persistence;

/// <summary>
/// Registration helpers for the shared EF Core model conventions.
/// </summary>
public static class ModelConfigurationBuilderExtensions
{
    /// <summary>
    /// Registers the shared model conventions (tenant shadow property and RowVersion/xmin
    /// mapping) for a module DbContext. Call from <c>DbContext.ConfigureConventions</c>.
    /// </summary>
    /// <param name="configurationBuilder">Model configuration builder of the DbContext.</param>
    /// <returns>The same <see cref="ModelConfigurationBuilder"/> for chaining.</returns>
    public static ModelConfigurationBuilder AddSharedModelConventions(
        this ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Conventions.Add(_ => new TenantIdShadowPropertyConvention());
        configurationBuilder.Conventions.Add(_ => new RowVersionedEntityConvention());

        // Both CLR types must be registered. ConfigureProperty (the path that applies pre-convention
        // property configuration) resolves by exact CLR type plus its base types and interfaces;
        // DateTimeOffset? inherits from ValueType, not from DateTimeOffset, so the non-nullable
        // registration alone would leave every nullable property without a converter.
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToUtcConverter>();
        configurationBuilder.Properties<DateTimeOffset?>().HaveConversion<NullableDateTimeOffsetToUtcConverter>();

        return configurationBuilder;
    }
}
