using Delobytes.App.Backend.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Delobytes.App.Backend.Identity.Infrastructure;

/// <summary>
/// Registers Identity module infrastructure services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Identity infrastructure services to the DI container.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="connectionString">Connection string.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddIdentityInfrastructure(this IServiceCollection services, string? connectionString)
    {
        ////string connectionString = configuration.GetConnectionString("DefaultConnection")
        ////    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");

        if (connectionString == null)
        {
            throw new InvalidOperationException("Connection string is not configured.");
        }

        services.AddDbContext<IdentityDbContext>(options =>
            options.UseNpgsql(connectionString, npgsqlOptions =>
                npgsqlOptions.MigrationsHistoryTable("__IdentityMigrationsHistory", "identity")));

        return services;
    }
}
