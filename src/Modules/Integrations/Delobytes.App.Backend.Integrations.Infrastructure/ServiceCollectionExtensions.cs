using Delobytes.App.Backend.Integrations.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Delobytes.App.Backend.Integrations.Infrastructure;

/// <summary>
/// Registers Integrations module infrastructure services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Integrations infrastructure services to the DI container.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="connectionString">Connection string.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddIntegrationsInfrastructure(this IServiceCollection services, string? connectionString)
    {
        if (connectionString == null)
        {
            throw new InvalidOperationException("Connection string is not configured.");
        }

        services.AddDbContext<IntegrationsDbContext>(options =>
            options.UseNpgsql(connectionString, npgsqlOptions =>
                npgsqlOptions.MigrationsHistoryTable("__IntegrationsMigrationsHistory", "integrations")));

        return services;
    }
}
