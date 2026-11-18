using Delobytes.App.Backend.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Delobytes.App.Backend.Finance.Infrastructure;

/// <summary>
/// Registers Finance module infrastructure services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Finance infrastructure services to the DI container.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="connectionString">Connection string.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddFinanceInfrastructure(this IServiceCollection services, string? connectionString)
    {
        if (connectionString == null)
        {
            throw new InvalidOperationException("Connection string is not configured.");
        }

        services.AddDbContext<FinanceDbContext>(options =>
            options.UseNpgsql(connectionString, npgsqlOptions =>
                npgsqlOptions.MigrationsHistoryTable("__FinanceMigrationsHistory", "finance")));

        return services;
    }
}
