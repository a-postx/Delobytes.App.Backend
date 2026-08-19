namespace Delobytes.App.Backend.Infrastructure;

using Delobytes.App.Backend.Catalog.Infrastructure;
using Delobytes.App.Backend.Identity.Infrastructure;
using Delobytes.App.Backend.Pricing.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddIdentityInfrastructure(configuration);
        services.AddCatalogInfrastructure(configuration);
        services.AddPricingInfrastructure(configuration);

        return services;
    }
}
