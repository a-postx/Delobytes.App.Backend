using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Infrastructure.Persistence;
using Delobytes.App.Backend.Catalog.Infrastructure.Persistence.Repositories;
using Delobytes.App.Backend.Contracts.Interfaces;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Delobytes.App.Backend.Tests.Infrastructure.Catalog;

public class BomLineRepositoryTests
{
    [Fact]
    public async Task GetEffectiveAtAsync_ReturnsLatestVersionEffectiveOnDate()
    {
        Guid tenantId = Guid.NewGuid();
        Guid productId = Guid.NewGuid();
        Guid componentId = Guid.NewGuid();
        DateOnly firstDate = new DateOnly(2026, 1, 1);
        DateOnly secondDate = new DateOnly(2026, 3, 1);
        DbContextOptions<CatalogDbContext> options = BuildOptions();

        using (CatalogDbContext context = BuildContext(options, tenantId))
        {
            context.BomLines.AddRange(
                BuildLine(productId, componentId, firstDate, 1m, DateTimeOffset.UtcNow.AddDays(-2)),
                BuildLine(productId, componentId, secondDate, 2m, DateTimeOffset.UtcNow.AddDays(-1)));
            await context.SaveChangesAsync();
        }

        using CatalogDbContext readContext = BuildContext(options, tenantId);
        BomLineRepository repository = new BomLineRepository(readContext);

        IReadOnlyList<BomLine> result = await repository.GetEffectiveAtAsync(
            productId,
            new DateOnly(2026, 2, 1),
            CancellationToken.None);

        BomLine line = result.Should().ContainSingle().Subject;
        line.ValidFrom.Should().Be(firstDate);
        line.Quantity.Should().Be(1m);
    }

    [Fact]
    public async Task GetEffectiveAtAsync_ReturnsVersionWhenDateEqualsValidFrom()
    {
        Guid tenantId = Guid.NewGuid();
        Guid productId = Guid.NewGuid();
        Guid componentId = Guid.NewGuid();
        DateOnly validFrom = new DateOnly(2026, 4, 15);
        DbContextOptions<CatalogDbContext> options = BuildOptions();

        using (CatalogDbContext context = BuildContext(options, tenantId))
        {
            context.BomLines.Add(BuildLine(productId, componentId, validFrom, 4m, DateTimeOffset.UtcNow));
            await context.SaveChangesAsync();
        }

        using CatalogDbContext readContext = BuildContext(options, tenantId);
        BomLineRepository repository = new BomLineRepository(readContext);

        IReadOnlyList<BomLine> result = await repository.GetEffectiveAtAsync(
            productId,
            validFrom,
            CancellationToken.None);

        result.Should().ContainSingle().Which.Quantity.Should().Be(4m);
    }

    [Fact]
    public async Task GetEffectiveAtAsync_ReturnsEmptyWhenDateIsBeforeFirstVersion()
    {
        Guid tenantId = Guid.NewGuid();
        Guid productId = Guid.NewGuid();
        Guid componentId = Guid.NewGuid();
        DbContextOptions<CatalogDbContext> options = BuildOptions();

        using (CatalogDbContext context = BuildContext(options, tenantId))
        {
            context.BomLines.Add(BuildLine(
                productId,
                componentId,
                new DateOnly(2026, 5, 1),
                1m,
                DateTimeOffset.UtcNow));
            await context.SaveChangesAsync();
        }

        using CatalogDbContext readContext = BuildContext(options, tenantId);
        BomLineRepository repository = new BomLineRepository(readContext);

        IReadOnlyList<BomLine> result = await repository.GetEffectiveAtAsync(
            productId,
            new DateOnly(2026, 4, 30),
            CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetEffectiveAtAsync_ReturnsEffectiveVersionForEachComponent()
    {
        Guid tenantId = Guid.NewGuid();
        Guid productId = Guid.NewGuid();
        Guid firstComponentId = Guid.NewGuid();
        Guid secondComponentId = Guid.NewGuid();
        DateOnly asOf = new DateOnly(2026, 6, 1);
        DbContextOptions<CatalogDbContext> options = BuildOptions();

        using (CatalogDbContext context = BuildContext(options, tenantId))
        {
            context.BomLines.AddRange(
                BuildLine(productId, firstComponentId, new DateOnly(2026, 1, 1), 1m, DateTimeOffset.UtcNow.AddDays(-4)),
                BuildLine(productId, firstComponentId, new DateOnly(2026, 5, 1), 2m, DateTimeOffset.UtcNow.AddDays(-3)),
                BuildLine(productId, secondComponentId, new DateOnly(2026, 2, 1), 3m, DateTimeOffset.UtcNow.AddDays(-2)));
            await context.SaveChangesAsync();
        }

        using CatalogDbContext readContext = BuildContext(options, tenantId);
        BomLineRepository repository = new BomLineRepository(readContext);

        IReadOnlyList<BomLine> result = await repository.GetEffectiveAtAsync(
            productId,
            asOf,
            CancellationToken.None);

        result.Should().HaveCount(2);
        result.Single(line => line.ComponentId == firstComponentId).Quantity.Should().Be(2m);
        result.Single(line => line.ComponentId == secondComponentId).Quantity.Should().Be(3m);
    }

    [Fact]
    public async Task GetEffectiveAtAsync_ExcludesAnotherTenant()
    {
        Guid tenantId = Guid.NewGuid();
        Guid otherTenantId = Guid.NewGuid();
        Guid productId = Guid.NewGuid();
        Guid componentId = Guid.NewGuid();
        DateOnly asOf = new DateOnly(2026, 6, 1);
        DbContextOptions<CatalogDbContext> options = BuildOptions();

        using (CatalogDbContext otherTenantContext = BuildContext(options, otherTenantId))
        {
            otherTenantContext.BomLines.Add(BuildLine(productId, componentId, new DateOnly(2026, 1, 1), 99m, DateTimeOffset.UtcNow));
            await otherTenantContext.SaveChangesAsync();
        }

        using CatalogDbContext context = BuildContext(options, tenantId);
        BomLineRepository repository = new BomLineRepository(context);

        IReadOnlyList<BomLine> result = await repository.GetEffectiveAtAsync(
            productId,
            asOf,
            CancellationToken.None);

        result.Should().BeEmpty();
    }

    private static DbContextOptions<CatalogDbContext> BuildOptions()
    {
        return new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase("bom-lines-" + Guid.NewGuid())
            .Options;
    }

    private static CatalogDbContext BuildContext(
        DbContextOptions<CatalogDbContext> options,
        Guid tenantId)
    {
        Mock<ITenantContext> tenantContext = new Mock<ITenantContext>();
        tenantContext.Setup(context => context.TenantId).Returns(tenantId);
        return new CatalogDbContext(options, tenantContext.Object);
    }

    private static BomLine BuildLine(
        Guid productId,
        Guid componentId,
        DateOnly validFrom,
        decimal quantity,
        DateTimeOffset createdAt)
    {
        return new BomLine
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            ComponentId = componentId,
            Quantity = quantity,
            ValidFrom = validFrom,
            IsActive = true,
            CreatedAt = createdAt,
        };
    }
}
