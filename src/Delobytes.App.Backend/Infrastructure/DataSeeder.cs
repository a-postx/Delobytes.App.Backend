using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

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
        Guid wildberriesId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        Guid ozonId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        Guid yandexKitId = Guid.Parse("33333333-3333-3333-3333-333333333333");

        SystemChannelTemplate[] templates = new[]
        {
            new SystemChannelTemplate
            {
                Id = wildberriesId,
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
                Id = ozonId,
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
                Id = yandexKitId,
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

        await SeedSystemChannelEndpointsAsync(context, logger, wildberriesId, ozonId);
    }

    /// <summary>
    /// Seeds SystemChannelEndpoint data for marketplace channels.
    /// </summary>
    private static async Task SeedSystemChannelEndpointsAsync(
        IntegrationsDbContext context,
        ILogger logger,
        Guid wildberriesId,
        Guid ozonId)
    {
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
}
