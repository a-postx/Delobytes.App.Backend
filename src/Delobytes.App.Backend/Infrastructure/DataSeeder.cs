using Delobytes.App.Backend.Integrations.Application.Options;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Delobytes.App.Backend.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NCrontab;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Persistence;

/// <summary>
/// Seeds initial data for the Integrations module.
/// </summary>
public static class DataSeeder
{
    /// <summary>
    /// Seeds SystemChannelTemplate data if it does not already exist.
    /// Idempotent: checks by Code and skips existing records.
    /// </summary>
    /// <param name="context">Integrations DbContext.</param>
    /// <param name="logger">Logger instance.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public static async Task SeedSystemChannelTemplatesAsync(IntegrationsDbContext context, ILogger logger)
    {
        SystemChannelTemplate[] templates = new[]
        {
            new SystemChannelTemplate
            {
                Id = Guid.NewGuid(),
                Code = "wildberries",
                DisplayName = "Wildberries",
                ApiBaseUrl = "https://suppliers-api.wildberries.ru",
                ApiVersion = "v1",
                Description = "Интеграция с Wildberries",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            },
            new SystemChannelTemplate
            {
                Id = Guid.NewGuid(),
                Code = "ozon",
                DisplayName = "Ozon",
                ApiBaseUrl = "https://api-seller.ozon.ru",
                ApiVersion = "v3",
                Description = "Интеграция с Ozon",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            },
            new SystemChannelTemplate
            {
                Id = Guid.NewGuid(),
                Code = "yandex.kit",
                DisplayName = "Яндекс.Кит",
                ApiBaseUrl = "https://api.kit.yandex.net/v1",
                ApiVersion = "v1",
                Description = "Интеграция с Яндекс.Кит",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            }
        };

        foreach (SystemChannelTemplate template in templates)
        {
            bool exists = await context.SystemChannelTemplates
                .AnyAsync(t => t.Code == template.Code);

            if (!exists)
            {
                context.SystemChannelTemplates.Add(template);
                logger.LogInformation("Seeding SystemChannelTemplate: {Code}", template.Code);
            }
            else
            {
                logger.LogDebug("SystemChannelTemplate {Code} already exists, skipping.", template.Code);
            }
        }

        int savedCount = await context.SaveChangesAsync();

        if (savedCount > 0)
        {
            logger.LogInformation("Seeded {Count} SystemChannelTemplate(s).", savedCount);
        }

        await SeedSystemChannelEndpointsAsync(context, logger);
    }

    /// <summary>
    /// Seeds SystemChannelEndpoint data for marketplace channels.
    /// </summary>
    private static async Task SeedSystemChannelEndpointsAsync(
        IntegrationsDbContext context,
        ILogger logger)
    {
        SystemChannelTemplate? wildberriesTemplate = await context.SystemChannelTemplates
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Code == "wildberries");

        SystemChannelTemplate? ozonTemplate = await context.SystemChannelTemplates
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Code == "ozon");

        if (wildberriesTemplate == null || ozonTemplate == null)
        {
            logger.LogWarning(
                "SystemChannelTemplates not found (WB={WbExists}, Ozon={OzExists}). Skipping endpoint seeding.",
                wildberriesTemplate != null,
                ozonTemplate != null);
            return;
        }

        Guid wildberriesId = wildberriesTemplate.Id;
        Guid ozonId = ozonTemplate.Id;

        SystemChannelEndpoint[] endpoints = new[]
        {
            // Wildberries endpoints
            new SystemChannelEndpoint
            {
                Id = Guid.NewGuid(),
                SystemChannelTemplateId = wildberriesId,
                EndpointType = ChannelEndpointType.Common,
                BaseUrl = "https://common-api.wildberries.ru",
                Description = "Общие операции WB (информация о продавце)",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            },
            new SystemChannelEndpoint
            {
                Id = Guid.NewGuid(),
                SystemChannelTemplateId = wildberriesId,
                EndpointType = ChannelEndpointType.Content,
                BaseUrl = "https://content-api.wildberries.ru",
                Description = "Управление каталогом и товарами WB",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            },
            new SystemChannelEndpoint
            {
                Id = Guid.NewGuid(),
                SystemChannelTemplateId = wildberriesId,
                EndpointType = ChannelEndpointType.Statistics,
                BaseUrl = "https://statistics-api.wildberries.ru",
                Description = "Статистика и отчеты WB",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            },
            new SystemChannelEndpoint
            {
                Id = Guid.NewGuid(),
                SystemChannelTemplateId = wildberriesId,
                EndpointType = ChannelEndpointType.Analytics,
                BaseUrl = "https://seller-analytics-api.wildberries.ru",
                Description = "Аналитика продавца WB",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            },
            new SystemChannelEndpoint
            {
                Id = Guid.NewGuid(),
                SystemChannelTemplateId = wildberriesId,
                EndpointType = ChannelEndpointType.Suppliers,
                BaseUrl = "https://suppliers-api.wildberries.ru",
                Description = "API поставщиков WB",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            },
            new SystemChannelEndpoint
            {
                Id = Guid.NewGuid(),
                SystemChannelTemplateId = wildberriesId,
                EndpointType = ChannelEndpointType.Prices,
                BaseUrl = "https://discounts-prices-api.wildberries.ru",
                Description = "Управление ценами WB",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            },
            new SystemChannelEndpoint
            {
                Id = Guid.NewGuid(),
                SystemChannelTemplateId = wildberriesId,
                EndpointType = ChannelEndpointType.Promotions,
                BaseUrl = "https://advert-api.wildberries.ru",
                Description = "Рекламные кампании WB",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            },

            // Ozon endpoint (single domain for all operations)
            new SystemChannelEndpoint
            {
                Id = Guid.NewGuid(),
                SystemChannelTemplateId = ozonId,
                EndpointType = ChannelEndpointType.Common,
                BaseUrl = "https://api-seller.ozon.ru",
                Description = "Все операции Ozon",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            }
        };

        foreach (SystemChannelEndpoint endpoint in endpoints)
        {
            bool exists = await context.Set<SystemChannelEndpoint>()
                .AnyAsync(e => e.SystemChannelTemplateId == endpoint.SystemChannelTemplateId
                               && e.EndpointType == endpoint.EndpointType);

            if (!exists)
            {
                context.Set<SystemChannelEndpoint>().Add(endpoint);
                logger.LogInformation(
                    "Seeding SystemChannelEndpoint: TemplateId={TemplateId}, Type={Type}",
                    endpoint.SystemChannelTemplateId,
                    endpoint.EndpointType);
            }
            else
            {
                logger.LogDebug(
                    "SystemChannelEndpoint already exists: TemplateId={TemplateId}, Type={Type}",
                    endpoint.SystemChannelTemplateId,
                    endpoint.EndpointType);
            }
        }

        int endpointsSaved = await context.SaveChangesAsync();

        if (endpointsSaved > 0)
        {
            logger.LogInformation("Seeded {Count} SystemChannelEndpoint(s).", endpointsSaved);
        }
    }

    /// <summary>
    /// Seeds a JobSchedule for OrdersImport (hourly by default) for every active Connection
    /// that does not already have one (Stage 11, decision "Seed initial schedules"). Bypasses
    /// the tenant query filter via IgnoreQueryFilters because this runs at startup with no
    /// ambient tenant; writes switch the ambient TenantId per connection so IntegrationsDbContext's
    /// SetTenantId guard can stamp the new row instead of throwing.
    /// </summary>
    /// <param name="context">Integrations DbContext.</param>
    /// <param name="tenantContext">Message-based tenant context, set per connection before each save.</param>
    /// <param name="options">Background jobs options (default cron expression).</param>
    /// <param name="logger">Logger instance.</param>
    public static async Task SeedJobSchedulesAsync(
        IntegrationsDbContext context,
        MessageTenantContext tenantContext,
        BackgroundJobsOptions options,
        ILogger logger)
    {
        List<Connection> activeConnections = await context.Connections
            .IgnoreQueryFilters()
            .Where(c => c.IsActive)
            .ToListAsync();

        HashSet<Guid> connectionsWithOrdersSchedule = (await context.JobSchedules
            .IgnoreQueryFilters()
            .Where(s => s.JobType == JobType.OrdersImport)
            .Select(s => s.ConnectionId)
            .ToListAsync())
            .ToHashSet();

        int seededCount = 0;

        foreach (Connection connection in activeConnections)
        {
            if (connectionsWithOrdersSchedule.Contains(connection.Id))
            {
                continue;
            }

            Guid tenantId = (Guid)context.Entry(connection).Property("TenantId").CurrentValue!;

            // Approximate: computed directly against UTC instead of the tenant's time zone.
            // SchedulerHostedService recomputes NextRunAt in the tenant's time zone the first
            // time this schedule actually fires, so the error only affects the very first run.
            DateTimeOffset now = DateTimeOffset.UtcNow;
            CrontabSchedule parsedSchedule = CrontabSchedule.Parse(options.DefaultOrdersImportCronExpression);
            DateTimeOffset nextRunAt = new DateTimeOffset(
                DateTime.SpecifyKind(parsedSchedule.GetNextOccurrence(now.UtcDateTime), DateTimeKind.Utc),
                TimeSpan.Zero);

            JobSchedule schedule = new JobSchedule
            {
                Id = Guid.NewGuid(),
                ConnectionId = connection.Id,
                JobType = JobType.OrdersImport,
                CronExpression = options.DefaultOrdersImportCronExpression,
                IsEnabled = true,
                LastRunAt = null,
                NextRunAt = nextRunAt,
            };

            tenantContext.SetTenantId(tenantId);

            try
            {
                context.JobSchedules.Add(schedule);
                await context.SaveChangesAsync();
                seededCount++;

                logger.LogInformation(
                    "Seeded JobSchedule for Connection {ConnectionId} (tenant {TenantId}), NextRunAt={NextRunAt}.",
                    connection.Id,
                    tenantId,
                    nextRunAt);
            }
            finally
            {
                tenantContext.Clear();
            }
        }

        if (seededCount > 0)
        {
            logger.LogInformation("Seeded {Count} JobSchedule(s).", seededCount);
        }
    }

    /// <summary>
    /// Seeds JobTypeConfiguration data (per-type watchdog timeout and batch size) if it does
    /// not already exist. Idempotent: checks by JobType and skips existing records.
    /// </summary>
    /// <param name="context">Integrations DbContext.</param>
    /// <param name="logger">Logger instance.</param>
    public static async Task SeedJobTypeConfigurationsAsync(IntegrationsDbContext context, ILogger logger)
    {
        JobTypeConfiguration[] configurations = new[]
        {
            new JobTypeConfiguration
            {
                JobType = JobType.ProductsImport,
                TimeoutMinutes = 60,
                BatchSize = 50,
            },
            new JobTypeConfiguration
            {
                JobType = JobType.OrdersImport,
                TimeoutMinutes = 30,
                BatchSize = 100,
            },
        };

        int seededCount = 0;

        foreach (JobTypeConfiguration configuration in configurations)
        {
            bool exists = await context.JobTypeConfigurations
                .AnyAsync(c => c.JobType == configuration.JobType);

            if (!exists)
            {
                context.JobTypeConfigurations.Add(configuration);
                seededCount++;
                logger.LogInformation("Seeding JobTypeConfiguration: {JobType}", configuration.JobType);
            }
            else
            {
                logger.LogDebug("JobTypeConfiguration {JobType} already exists, skipping.", configuration.JobType);
            }
        }

        if (seededCount > 0)
        {
            await context.SaveChangesAsync();
            logger.LogInformation("Seeded {Count} JobTypeConfiguration(s).", seededCount);
        }
    }
}
