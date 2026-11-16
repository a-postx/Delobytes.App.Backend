using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
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
            context.Components.Add(BuildComponent(componentId));
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
            context.Components.Add(BuildComponent(componentId));
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
            context.Components.Add(BuildComponent(componentId));
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
            context.Components.AddRange(
                BuildComponent(firstComponentId),
                BuildComponent(secondComponentId));
            context.BomLines.AddRange(
                BuildLine(productId, firstComponentId, new DateOnly(2026, 1, 1), 1m, DateTimeOffset.UtcNow.AddDays(-4), new DateOnly(2026, 5, 1)),
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
            otherTenantContext.Components.Add(BuildComponent(componentId));
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

    [Fact]
    public async Task GetEffectiveAtAsync_ExcludesComponentWhoseVersionEndedBeforeTheDate()
    {
        Guid tenantId = Guid.NewGuid();
        Guid productId = Guid.NewGuid();
        Guid keptComponentId = Guid.NewGuid();
        Guid removedComponentId = Guid.NewGuid();
        DateOnly removalDate = new DateOnly(2026, 3, 1);
        DbContextOptions<CatalogDbContext> options = BuildOptions();

        using (CatalogDbContext context = BuildContext(options, tenantId))
        {
            context.Components.AddRange(
                BuildComponent(keptComponentId),
                BuildComponent(removedComponentId));

            // A composition of two components, replaced on removalDate by one that keeps a single
            // component. Both rows of the replaced composition are closed on that date, exactly as
            // the upsert handler closes them.
            context.BomLines.AddRange(
                BuildLine(productId, keptComponentId, new DateOnly(2026, 1, 1), 1m, DateTimeOffset.UtcNow.AddDays(-10), removalDate),
                BuildLine(productId, removedComponentId, new DateOnly(2026, 1, 1), 5m, DateTimeOffset.UtcNow.AddDays(-10), removalDate),
                BuildLine(productId, keptComponentId, removalDate, 2m, DateTimeOffset.UtcNow.AddDays(-9)));
            await context.SaveChangesAsync();
        }

        using CatalogDbContext readContext = BuildContext(options, tenantId);
        BomLineRepository repository = new BomLineRepository(readContext);

        IReadOnlyList<BomLine> afterRemoval = await repository.GetEffectiveAtAsync(
            productId,
            new DateOnly(2026, 4, 1),
            CancellationToken.None);

        // The removed component must not be charged for a date after it left the composition. This is
        // the regression the flag-based model allowed: it recorded that the component was gone but
        // not when, so the resolver kept counting it indefinitely.
        afterRemoval.Should().ContainSingle();
        afterRemoval[0].ComponentId.Should().Be(keptComponentId);
        afterRemoval[0].Quantity.Should().Be(2m);

        IReadOnlyList<BomLine> beforeRemoval = await repository.GetEffectiveAtAsync(
            productId,
            new DateOnly(2026, 2, 1),
            CancellationToken.None);

        // Before the removal both components are still charged, with the quantities of that version.
        beforeRemoval.Should().HaveCount(2);
        beforeRemoval.Single(line => line.ComponentId == removedComponentId).Quantity.Should().Be(5m);
        beforeRemoval.Single(line => line.ComponentId == keptComponentId).Quantity.Should().Be(1m);
    }

    [Fact]
    public async Task GetEffectiveAtAsync_ExcludesVersionOnItsValidToDate()
    {
        Guid tenantId = Guid.NewGuid();
        Guid productId = Guid.NewGuid();
        Guid componentId = Guid.NewGuid();
        DateOnly validTo = new DateOnly(2026, 3, 1);
        DbContextOptions<CatalogDbContext> options = BuildOptions();

        using (CatalogDbContext context = BuildContext(options, tenantId))
        {
            context.Components.Add(BuildComponent(componentId));
            context.BomLines.Add(BuildLine(
                productId,
                componentId,
                new DateOnly(2026, 1, 1),
                3m,
                DateTimeOffset.UtcNow,
                validTo));
            await context.SaveChangesAsync();
        }

        using CatalogDbContext readContext = BuildContext(options, tenantId);
        BomLineRepository repository = new BomLineRepository(readContext);

        IReadOnlyList<BomLine> onLastDay = await repository.GetEffectiveAtAsync(
            productId,
            validTo.AddDays(-1),
            CancellationToken.None);

        IReadOnlyList<BomLine> onValidTo = await repository.GetEffectiveAtAsync(
            productId,
            validTo,
            CancellationToken.None);

        // Half-open interval: ValidTo is the first date on which the version no longer applies, so
        // the day before it still counts and the day itself does not.
        onLastDay.Should().ContainSingle().Which.Quantity.Should().Be(3m);
        onValidTo.Should().BeEmpty();
    }

    [Fact]
    public async Task GetOpenByProductIdAsync_ReturnsOnlyVersionsStillInForce()
    {
        Guid tenantId = Guid.NewGuid();
        Guid productId = Guid.NewGuid();
        Guid componentId = Guid.NewGuid();
        DbContextOptions<CatalogDbContext> options = BuildOptions();

        using (CatalogDbContext context = BuildContext(options, tenantId))
        {
            context.Components.Add(BuildComponent(componentId));
            context.BomLines.AddRange(
                BuildLine(productId, componentId, new DateOnly(2026, 1, 1), 1m, DateTimeOffset.UtcNow.AddDays(-2), new DateOnly(2026, 2, 1)),
                BuildLine(productId, componentId, new DateOnly(2026, 2, 1), 2m, DateTimeOffset.UtcNow.AddDays(-1)));
            await context.SaveChangesAsync();
        }

        using CatalogDbContext readContext = BuildContext(options, tenantId);
        BomLineRepository repository = new BomLineRepository(readContext);

        IReadOnlyList<BomLine> result = await repository.GetOpenByProductIdAsync(productId, CancellationToken.None);

        result.Should().ContainSingle();
        result[0].Quantity.Should().Be(2m);
        result[0].ValidTo.Should().BeNull();
    }

    [Fact]
    public async Task GetProductIdsByComponentIdAsync_ExcludesProductsThatNoLongerUseTheComponent()
    {
        Guid tenantId = Guid.NewGuid();
        Guid liveProductId = Guid.NewGuid();
        Guid removedProductId = Guid.NewGuid();
        Guid componentId = Guid.NewGuid();
        DbContextOptions<CatalogDbContext> options = BuildOptions();

        using (CatalogDbContext context = BuildContext(options, tenantId))
        {
            context.Components.Add(BuildComponent(componentId));
            context.BomLines.AddRange(
                BuildLine(liveProductId, componentId, new DateOnly(2026, 1, 1), 1m, DateTimeOffset.UtcNow.AddDays(-2)),
                BuildLine(removedProductId, componentId, new DateOnly(2026, 1, 1), 1m, DateTimeOffset.UtcNow.AddDays(-2), new DateOnly(2026, 2, 1)));
            await context.SaveChangesAsync();
        }

        using CatalogDbContext readContext = BuildContext(options, tenantId);
        BomLineRepository repository = new BomLineRepository(readContext);

        IReadOnlyList<Guid> productIds = await repository.GetProductIdsByComponentIdAsync(componentId, CancellationToken.None);

        // A product that dropped the component needs no cost snapshot when that component's price
        // changes: the component is no longer part of its cost.
        productIds.Should().ContainSingle().Which.Should().Be(liveProductId);
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
        DateTimeOffset createdAt,
        DateOnly? validTo = null)
    {
        return new BomLine
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            ComponentId = componentId,
            Quantity = quantity,
            ValidFrom = validFrom,
            ValidTo = validTo,
            CreatedAt = createdAt,
        };
    }

    // BomLine.ComponentId is a required foreign key, so a composition row without its
    // component cannot exist in Postgres. The in-memory provider does not enforce that
    // constraint, which lets a fixture with a dangling ComponentId pass here while the
    // exact same data would be rejected by the real database. Every fixture therefore
    // creates the components it references.
    private static Component BuildComponent(Guid id)
    {
        return new Component
        {
            Id = id,
            Name = "Компонент " + id.ToString("N").Substring(0, 8),
            Unit = Unit.Piece,
            Category = ComponentCategory.Material,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }
}
