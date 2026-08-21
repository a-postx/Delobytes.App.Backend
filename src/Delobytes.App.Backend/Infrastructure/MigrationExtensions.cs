using Delobytes.App.Backend.Catalog.Infrastructure.Persistence;
using Delobytes.App.Backend.Identity.Infrastructure.Persistence;
using Delobytes.App.Backend.Pricing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Infrastructure;

/// <summary>
/// Extension methods for applying EF Core migrations on application startup.
/// </summary>
public static class MigrationExtensions
{
    /// <summary>
    /// Applies pending EF Core migrations for all module DbContexts.
    /// Safe to call on every startup (no-op if migrations are already applied).
    /// </summary>
    /// <param name="serviceProvider">Root service provider.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public static async Task ApplyMigrationsAsync(this IServiceProvider serviceProvider)
    {
        using IServiceScope scope = serviceProvider.CreateScope();
        ILogger<Program> logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

        await ApplyAsync<IdentityDbContext>(scope, logger, "Identity");
        await ApplyAsync<CatalogDbContext>(scope, logger, "Catalog");
        await ApplyAsync<PricingDbContext>(scope, logger, "Pricing");
    }

    private static async Task ApplyAsync<TContext>(IServiceScope scope, ILogger logger, string moduleName) where TContext : DbContext
    {
        try
        {
            TContext context = scope.ServiceProvider.GetRequiredService<TContext>();
            IEnumerable<string> pending = await context.Database.GetPendingMigrationsAsync();

            if (pending.Any())
            {
                logger.LogInformation("Applying {Count} pending migration(s) for module {Module}...", pending.Count(), moduleName);
                await context.Database.MigrateAsync();
                logger.LogInformation("Migrations applied for module {Module}.", moduleName);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not apply migrations for module {Module}. Database may not be available.", moduleName);
        }
    }
}
