using Delobytes.App.Backend.Catalog.Infrastructure.Persistence;
using Delobytes.App.Backend.Identity.Application.Options;
using Delobytes.App.Backend.Identity.Domain.Entities;
using Delobytes.App.Backend.Identity.Infrastructure.Persistence;
using Delobytes.App.Backend.Integrations.Application.Options;
using Delobytes.App.Backend.Integrations.Infrastructure.Persistence;
using Delobytes.App.Backend.Sales.Infrastructure.Persistence;
using Delobytes.App.Backend.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
        await ApplyAsync<IntegrationsDbContext>(scope, logger, "Integrations");
        await ApplyAsync<CatalogDbContext>(scope, logger, "Catalog");
        await ApplyAsync<SalesDbContext>(scope, logger, "Sales");

        await SeedIdentityDataAsync(scope, logger);
        await SeedIntegrationsDataAsync(scope, logger);
    }

    // Backfills Tenant.MaxBackgroundJobsParallelism (Stage 11) for tenants created before the
    // field existed. CreateTenantCommandHandler already sets it for new tenants from
    // MultitenancyOptions.DefaultMaxBackgroundJobsParallelism; this only fixes up the backlog.
    private static async Task SeedIdentityDataAsync(IServiceScope scope, ILogger logger)
    {
        try
        {
            IdentityDbContext context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

            bool canConnect = await context.Database.CanConnectAsync();
            if (!canConnect)
            {
                logger.LogWarning("Cannot connect to Identity database. Skipping data seeding.");
                return;
            }

            IEnumerable<string> appliedMigrations = await context.Database.GetAppliedMigrationsAsync();
            if (!appliedMigrations.Any())
            {
                logger.LogWarning("Identity module has no applied migrations. Skipping data seeding. Run migrations first.");
                return;
            }

            IEnumerable<string> pendingMigrations = await context.Database.GetPendingMigrationsAsync();
            if (pendingMigrations.Any())
            {
                logger.LogWarning("Identity module has pending migrations. Skipping data seeding until migrations are applied.");
                return;
            }

            MultitenancyOptions options = scope.ServiceProvider.GetRequiredService<IOptions<MultitenancyOptions>>().Value;

            List<Tenant> tenantsMissingParallelism = await context.Tenants
                .Where(t => t.MaxBackgroundJobsParallelism <= 0)
                .ToListAsync();

            if (tenantsMissingParallelism.Count == 0)
            {
                return;
            }

            foreach (Tenant tenant in tenantsMissingParallelism)
            {
                tenant.MaxBackgroundJobsParallelism = options.DefaultMaxBackgroundJobsParallelism;
            }

            await context.SaveChangesAsync();

            logger.LogInformation(
                "Seeded MaxBackgroundJobsParallelism={Value} for {Count} existing tenant(s).",
                options.DefaultMaxBackgroundJobsParallelism,
                tenantsMissingParallelism.Count);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not seed Identity data. Database may not be available.");
        }
    }

    private static async Task SeedIntegrationsDataAsync(IServiceScope scope, ILogger logger)
    {
        try
        {
            IntegrationsDbContext context = scope.ServiceProvider.GetRequiredService<IntegrationsDbContext>();

            bool canConnect = await context.Database.CanConnectAsync();
            if (!canConnect)
            {
                logger.LogWarning("Cannot connect to Integrations database. Skipping data seeding.");
                return;
            }

            IEnumerable<string> appliedMigrations = await context.Database.GetAppliedMigrationsAsync();
            if (!appliedMigrations.Any())
            {
                logger.LogWarning("Integrations module has no applied migrations. Skipping data seeding. Run migrations first.");
                return;
            }

            IEnumerable<string> pendingMigrations = await context.Database.GetPendingMigrationsAsync();
            if (pendingMigrations.Any())
            {
                logger.LogWarning("Integrations module has pending migrations. Skipping data seeding until migrations are applied.");
                return;
            }

            await DataSeeder.SeedSystemChannelTemplatesAsync(context, logger);

            MessageTenantContext tenantContext = scope.ServiceProvider.GetRequiredService<MessageTenantContext>();
            BackgroundJobsOptions backgroundJobsOptions = scope.ServiceProvider.GetRequiredService<IOptions<BackgroundJobsOptions>>().Value;

            await DataSeeder.SeedJobSchedulesAsync(context, tenantContext, backgroundJobsOptions, logger);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not seed Integrations data. Database may not be available.");
        }
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
