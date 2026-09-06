using Delobytes.App.Backend.Sales.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Delobytes.App.Backend.Sales.Infrastructure;

/// <summary>
/// Registers Sales module infrastructure services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Sales infrastructure services to the DI container.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="connectionString">Connection string.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddSalesInfrastructure(this IServiceCollection services, string? connectionString)
    {
        if (connectionString == null)
        {
            throw new InvalidOperationException("Connection string is not configured.");
        }

        services.AddDbContext<SalesDbContext>(options =>
            options.UseNpgsql(connectionString, npgsqlOptions =>
                npgsqlOptions.MigrationsHistoryTable("__SalesMigrationsHistory", "sales")));

        return services;
    }
}
