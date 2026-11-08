using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Infrastructure.Persistence;
using Delobytes.App.Backend.Catalog.Infrastructure.Persistence.Repositories;
using Delobytes.App.Backend.Contracts.Accounting;
using Delobytes.App.Backend.Contracts.Interfaces;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Delobytes.App.Backend.Tests.Infrastructure.Catalog;

public class ChannelParameterSetRepositoryTests
{
    [Fact]
    public async Task GetActiveByChannelIdAsync_UsesTenantTimeZone_NotUtc()
    {
        Guid tenantId = Guid.NewGuid();
        Guid channelId = Guid.NewGuid();
        DbContextOptions<CatalogDbContext> options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase("channel-parameter-set-tests-" + Guid.NewGuid())
            .Options;

        // 22:30 UTC is already the next calendar day in Samara (UTC+4), so a UTC-based "today"
        // would keep yesterday's parameter set active.
        DateTimeOffset utcNow = new DateTimeOffset(2026, 11, 8, 22, 30, 0, TimeSpan.Zero);
        DateOnly tenantLocalToday = new DateOnly(2026, 11, 9);

        using (CatalogDbContext seedContext = BuildContext(options, tenantId))
        {
            seedContext.ChannelParameterSets.AddRange(
                BuildParameterSet(channelId, tenantLocalToday.AddYears(-1)),
                BuildParameterSet(channelId, tenantLocalToday),
                BuildParameterSet(channelId, tenantLocalToday.AddDays(1)));
            await seedContext.SaveChangesAsync();
        }

        Mock<ITenantContext> tenantContextMock = new Mock<ITenantContext>();
        tenantContextMock.Setup(context => context.TenantId).Returns(tenantId);

        Mock<ITenantSettingsProvider> settingsProviderMock = new Mock<ITenantSettingsProvider>();
        settingsProviderMock
            .Setup(provider => provider.GetAccountingSettingsAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantAccountingSettings { Currency = "RUB", TimeZoneId = "Europe/Samara" });

        using CatalogDbContext readContext = BuildContext(options, tenantId);
        ChannelParameterSetRepository repository = new ChannelParameterSetRepository(
            readContext,
            tenantContextMock.Object,
            settingsProviderMock.Object,
            new FixedTimeProvider(utcNow));

        ChannelParameterSet? active = await repository.GetActiveByChannelIdAsync(channelId, CancellationToken.None);

        active.Should().NotBeNull();
        active!.ValidFrom.Should().Be(tenantLocalToday);
    }

    private static CatalogDbContext BuildContext(DbContextOptions<CatalogDbContext> options, Guid tenantId)
    {
        Mock<ITenantContext> tenantContextMock = new Mock<ITenantContext>();
        tenantContextMock.Setup(context => context.TenantId).Returns(tenantId);
        return new CatalogDbContext(options, tenantContextMock.Object);
    }

    private static ChannelParameterSet BuildParameterSet(Guid channelId, DateOnly validFrom)
    {
        return new ChannelParameterSet
        {
            Id = Guid.NewGuid(),
            ChannelId = channelId,
            ValidFrom = validFrom,
            CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        };
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow()
        {
            return _utcNow;
        }
    }
}
