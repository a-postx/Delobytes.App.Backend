using Delobytes.App.Backend.Integrations.Domain.Entities;
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
    }
}
