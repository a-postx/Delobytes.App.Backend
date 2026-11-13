using Delobytes.App.Backend.Integrations.Application.Jobs;
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
                Description = "РРЅС‚РµРіСЂР°С†РёСЏ СЃ Wildberries",
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
                Description = "РРЅС‚РµРіСЂР°С†РёСЏ СЃ Ozon",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            },
            new SystemChannelTemplate
            {
                Id = Guid.NewGuid(),
                Code = "yandex.kit",
                DisplayName = "РЇРЅРґРµРєСЃ.РљРёС‚",
                ApiBaseUrl = "https://api.kit.yandex.net/v1",
                ApiVersion = "v1",
                Description = "РРЅС‚РµРіСЂР°С†РёСЏ СЃ РЇРЅРґРµРєСЃ.РљРёС‚",
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
                Description = "РћР±С‰РёРµ РѕРїРµСЂР°С†РёРё WB (РёРЅС„РѕСЂРјР°С†РёСЏ Рѕ РїСЂРѕРґР°РІС†Рµ)",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            },
            new SystemChannelEndpoint
            {
                Id = Guid.NewGuid(),
                SystemChannelTemplateId = wildberriesId,
                EndpointType = ChannelEndpointType.Content,
                BaseUrl = "https://content-api.wildberries.ru",
                Description = "РЈРїСЂР°РІР»РµРЅРёРµ РєР°С‚Р°Р»РѕРіРѕРј Рё С‚РѕРІР°СЂР°РјРё WB",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            },
            new SystemChannelEndpoint
            {
                Id = Guid.NewGuid(),
                SystemChannelTemplateId = wildberriesId,
                EndpointType = ChannelEndpointType.Statistics,
                BaseUrl = "https://statistics-api.wildberries.ru",
                Description = "РЎС‚Р°С‚РёСЃС‚РёРєР° Рё РѕС‚С‡РµС‚С‹ WB",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            },
            new SystemChannelEndpoint
            {
                Id = Guid.NewGuid(),
                SystemChannelTemplateId = wildberriesId,
                EndpointType = ChannelEndpointType.Analytics,
                BaseUrl = "https://seller-analytics-api.wildberries.ru",
                Description = "РђРЅР°Р»РёС‚РёРєР° РїСЂРѕРґР°РІС†Р° WB",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            },
            new SystemChannelEndpoint
            {
                Id = Guid.NewGuid(),
                SystemChannelTemplateId = wildberriesId,
                EndpointType = ChannelEndpointType.Suppliers,
                BaseUrl = "https://suppliers-api.wildberries.ru",
                Description = "API РїРѕСЃС‚Р°РІС‰РёРєРѕРІ WB",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            },
            new SystemChannelEndpoint
            {
                Id = Guid.NewGuid(),
                SystemChannelTemplateId = wildberriesId,
                EndpointType = ChannelEndpointType.Prices,
                BaseUrl = "https://discounts-prices-api.wildberries.ru",
                Description = "РЈРїСЂР°РІР»РµРЅРёРµ С†РµРЅР°РјРё WB",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            },
            new SystemChannelEndpoint
            {
                Id = Guid.NewGuid(),
                SystemChannelTemplateId = wildberriesId,
                EndpointType = ChannelEndpointType.Promotions,
                BaseUrl = "https://advert-api.wildberries.ru",
                Description = "Р РµРєР»Р°РјРЅС‹Рµ РєР°РјРїР°РЅРёРё WB",
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
                Description = "Р’СЃРµ РѕРїРµСЂР°С†РёРё Ozon",
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
    /// Seeds the orders JobSchedule matching each active Connection's channel (hourly by default)
    /// when it does not already have one, and disables a stale OrdersImport schedule left on a
    /// non-Wildberries connection by earlier versions of this seeder. Bypasses the tenant query
    /// filter via IgnoreQueryFilters because this runs at startup with no ambient tenant; writes
    /// switch the ambient TenantId per connection so IntegrationsDbContext's SetTenantId guard can
    /// stamp the new row instead of throwing.
    /// </summary>
    /// <param name="context">Integrations DbContext.</param>
    /// <param name="tenantContext">Message-based tenant context, set per connection before each save.</param>
    /// <param name="options">Background jobs options (default cron expression).</param>
    /// <param name="logger">Logger instance.</param>
    public static async Task SeedJobSchedulesAsync(
        IntegrationsDbContext context,
        MessageTenantContext tenantContext,
        BackgroundJobsOptions options,
        IChannelJobTypeRegistry jobTypes,
        ILogger logger)
    {
        List<Connection> activeConnections = await context.Connections
            .IgnoreQueryFilters()
            .Include(c => c.SystemChannelTemplate)
            .Where(c => c.IsActive)
            .ToListAsync();

        List<JobSchedule> existingSchedules = await context.JobSchedules
            .IgnoreQueryFilters()
            .ToListAsync();

        await DisableStaleOrdersSchedulesAsync(
            context,
            tenantContext,
            activeConnections,
            existingSchedules,
            jobTypes,
            logger);

        int seededCount = 0;

        // Approximate: computed directly against UTC instead of the tenant's time zone.
        // SchedulerHostedService recomputes NextRunAt in the tenant's time zone the first
        // time this schedule actually fires, so the error only affects the very first run.
        CrontabSchedule parsedSchedule = CrontabSchedule.Parse(options.DefaultOrdersImportCronExpression);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset defaultNextRunAt = new DateTimeOffset(
            DateTime.SpecifyKind(parsedSchedule.GetNextOccurrence(now.UtcDateTime), DateTimeKind.Utc),
            TimeSpan.Zero);

        foreach (Connection connection in activeConnections)
        {
            string? channelCode = connection.SystemChannelTemplate?.Code;

            foreach (JobType jobType in jobTypes.GetJobTypes(channelCode, ChannelJobKind.OrdersImport))
            {
                if (existingSchedules.Exists(s => s.ConnectionId == connection.Id && s.JobType == jobType))
                {
                    continue;
                }

                Guid tenantId = (Guid)context.Entry(connection).Property("TenantId").CurrentValue!;

                JobSchedule schedule = new JobSchedule
                {
                    Id = Guid.NewGuid(),
                    ConnectionId = connection.Id,
                    JobType = jobType,
                    CronExpression = options.DefaultOrdersImportCronExpression,
                    IsEnabled = true,
                    LastRunAt = null,
                    NextRunAt = defaultNextRunAt,
                };

                tenantContext.SetTenantId(tenantId);

                try
                {
                    context.JobSchedules.Add(schedule);
                    await context.SaveChangesAsync();
                    seededCount++;
                    existingSchedules.Add(schedule);

                    logger.LogInformation(
                        "Seeded JobSchedule {JobType} for Connection {ConnectionId} (tenant {TenantId}), NextRunAt={NextRunAt}.",
                        jobType,
                        connection.Id,
                        tenantId,
                        defaultNextRunAt);
                }
                finally
                {
                    tenantContext.Clear();
                }
            }
        }

        if (seededCount > 0)
        {
            logger.LogInformation("Seeded {Count} JobSchedule(s).", seededCount);
        }
    }

    /// <summary>
    /// Disables an enabled <see cref="JobType.WbOrdersImport"/> schedule found on a connection whose
    /// channel no longer maps to that job type (e.g. Ozon, after earlier versions of this seeder
    /// created it for every channel indiscriminately). Leaves the row in place, disabled, so an
    /// operator can see what happened instead of having it silently disappear.
    /// </summary>
    private static async Task DisableStaleOrdersSchedulesAsync(
        IntegrationsDbContext context,
        MessageTenantContext tenantContext,
        List<Connection> activeConnections,
        List<JobSchedule> existingSchedules,
        IChannelJobTypeRegistry jobTypes,
        ILogger logger)
    {
        Dictionary<Guid, string?> channelCodeByConnectionId = activeConnections
            .ToDictionary(c => c.Id, c => c.SystemChannelTemplate?.Code);

        foreach (JobSchedule schedule in existingSchedules)
        {
            if (schedule.JobType != JobType.WbOrdersImport || !schedule.IsEnabled)
            {
                continue;
            }

            if (!channelCodeByConnectionId.TryGetValue(schedule.ConnectionId, out string? channelCode))
            {
                continue;
            }

            bool stillBelongsHere = jobTypes.IsServedByChannel(channelCode, JobType.WbOrdersImport);

            if (stillBelongsHere)
            {
                continue;
            }

            schedule.IsEnabled = false;

            tenantContext.SetTenantId(schedule.TenantId);

            try
            {
                await context.SaveChangesAsync();

                logger.LogWarning(
                    "DataSeeder: disabled stale OrdersImport schedule {ScheduleId} for connection " +
                    "{ConnectionId} (channel '{ChannelCode}'); that channel no longer runs OrdersImport.",
                    schedule.Id,
                    schedule.ConnectionId,
                    channelCode);
            }
            finally
            {
                tenantContext.Clear();
            }
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
                JobType = JobType.WbOrdersImport,
                TimeoutMinutes = 30,
                BatchSize = 100,
            },
            new JobTypeConfiguration
            {
                JobType = JobType.OzonOrdersImportFbo,
                TimeoutMinutes = 30,
                BatchSize = 100,
            },
            new JobTypeConfiguration
            {
                JobType = JobType.OzonOrdersImportFbs,
                TimeoutMinutes = 30,
                BatchSize = 100,
            },
            new JobTypeConfiguration
            {
                // BatchSize is seeded for consistency with the channel's products tuning only:
                // ProductsImportOrchestrator reads ProductsImportTuning, not this row.
                JobType = JobType.OzonProductsImport,
                TimeoutMinutes = 60,
                BatchSize = 200,
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
