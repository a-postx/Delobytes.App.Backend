using Delobytes.App.Backend.Catalog.Infrastructure.Persistence;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Finance.Infrastructure.Persistence;
using Delobytes.App.Backend.Identity.Infrastructure.Persistence;
using Delobytes.App.Backend.Integrations.Infrastructure.Persistence;
using Delobytes.App.Backend.Sales.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Infrastructure;

/// <summary>
/// Applies EF Core migrations and module data backfills on application startup.
/// </summary>
public static class MigrationExtensions
{
    /// <summary>
    /// Applies pending EF Core migrations for all module DbContexts and then runs every registered
    /// <see cref="IDataBackfill"/>. Safe to call on every startup: migrations are a no-op once
    /// applied and backfills are idempotent. A failure is not swallowed — the caller logs it and
    /// stops the host rather than serving on an outdated schema.
    /// </summary>
    /// <param name="serviceProvider">Root service provider.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public static async Task MigrateAsync(this IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
    {
        using IServiceScope scope = serviceProvider.CreateScope();
        ILogger<Program> logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

        await ApplyAsync<IdentityDbContext>(scope, logger, "Identity", cancellationToken);
        await ApplyAsync<IntegrationsDbContext>(scope, logger, "Integrations", cancellationToken);
        await ApplyAsync<CatalogDbContext>(scope, logger, "Catalog", cancellationToken);
        await ApplyAsync<SalesDbContext>(scope, logger, "Sales", cancellationToken);
        await ApplyAsync<FinanceDbContext>(scope, logger, "Finance", cancellationToken);

        foreach (IDataBackfill backfill in scope.ServiceProvider.GetServices<IDataBackfill>())
        {
            logger.LogInformation("Running data backfill: {Backfill}", backfill.Name);
            await backfill.RunAsync(cancellationToken);
            logger.LogInformation("Data backfill finished: {Backfill}", backfill.Name);
        }
    }

    private static async Task ApplyAsync<TContext>(
        IServiceScope scope,
        ILogger logger,
        string moduleName,
        CancellationToken cancellationToken)
        where TContext : DbContext
    {
        // Migration failures must stop startup: the caller logs the exception and exits with a
        // non-zero code instead of serving on a half-upgraded schema.
        TContext context = scope.ServiceProvider.GetRequiredService<TContext>();
        List<string> pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();

        if (pending.Count == 0)
        {
            logger.LogInformation("Module {Module} has no pending migrations.", moduleName);
            return;
        }

        logger.LogInformation("Applying {Count} pending migration(s) for module {Module}...", pending.Count, moduleName);
        await context.Database.MigrateAsync(cancellationToken);
        logger.LogInformation("Migrations applied for module {Module}.", moduleName);
    }
}
