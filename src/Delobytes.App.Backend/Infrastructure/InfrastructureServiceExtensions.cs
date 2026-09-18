using Delobytes.App.Backend.Catalog.Infrastructure;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Identity.Infrastructure;
using Delobytes.App.Backend.Identity.Infrastructure.Services;
using Delobytes.App.Backend.Integrations.Infrastructure;
using Delobytes.App.Backend.Options;
using Delobytes.App.Backend.Sales.Infrastructure;
using Delobytes.App.Backend.Services;

namespace Delobytes.App.Backend.Infrastructure;

/// <summary>
/// Shared infrastructure registration entry point for the Web API host.
/// </summary>
public static class InfrastructureServiceExtensions
{
    /// <summary>
    /// Registers all module infrastructure services.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <param name="secrets">App secrets.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration, AppSecrets? secrets)
    {
        services.AddHttpContextAccessor();

        // Register tenant contexts
        services.AddScoped<MessageTenantContext>();
        services.AddScoped<ITenantContext>(sp => new CompositeTenantContext(
            sp.GetRequiredService<IHttpContextAccessor>(),
            sp.GetRequiredService<MessageTenantContext>()));

        services.AddIdentityInfrastructure(
            configuration,
            secrets?.ConnectionString,
            secrets?.JwtSecretKey,
            secrets?.YandexClientId,
            secrets?.YandexClientSecret,
            secrets?.GoogleClientId,
            secrets?.GoogleClientSecret);
        services.AddCatalogInfrastructure(secrets?.ConnectionString);
        services.AddSalesInfrastructure(secrets?.ConnectionString);
        services.AddIntegrationsInfrastructure(secrets?.ConnectionString);

        return services;
    }
}
