using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Catalog.Application.Commands.BomLines.CreateBomLine;
using Delobytes.App.Backend.Catalog.Application.Commands.BomLines.UpsertProductBom;
using Delobytes.App.Backend.Catalog.Application.Commands.Components.CreateComponentPrice;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Application.Services.CostCalculation;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using Delobytes.App.Backend.Catalog.Infrastructure.Persistence;
using Delobytes.App.Backend.Catalog.Infrastructure.Persistence.Repositories;
using Delobytes.App.Backend.Contracts.Interfaces;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Catalog.CostCalculation;

/// <summary>
/// End-to-end checks for the automatic snapshot capture added in Этап 5.
///
/// The handlers are wired to the real repositories, the real calculator and the real snapshot
/// service over an in-memory database. A mock-only test can confirm that CaptureBeforeChangeAsync
/// was invoked, but it cannot confirm that the figures captured were the ones in force *before*
/// the change — that only shows up when the snapshot is actually calculated against the same
/// DbContext that is about to be mutated.
/// </summary>
public class CostSnapshotTriggerIntegrationTests : IDisposable
{
    private readonly CatalogDbContext _context;
    private readonly ProductCostSnapshotService _snapshotService;
    private readonly BomLineRepository _bomLineRepository;
    private readonly ComponentPriceRepository _componentPriceRepository;
    private readonly ProductWorkRateRepository _productWorkRateRepository;
    private readonly WorkRateRepository _workRateRepository;
    private readonly ComponentRepository _componentRepository;
    private readonly DateOnly _today;

    public CostSnapshotTriggerIntegrationTests()
    {
        DbContextOptions<CatalogDbContext> options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase("catalog-cost-snapshot-trigger-" + Guid.NewGuid())
            .Options;

        Mock<ITenantContext> tenantContextMock = new Mock<ITenantContext>();
        tenantContextMock.Setup(context => context.TenantId).Returns(Guid.NewGuid());

        _context = new CatalogDbContext(options, tenantContextMock.Object);

        _bomLineRepository = new BomLineRepository(_context);
        _componentPriceRepository = new ComponentPriceRepository(_context);
        _productWorkRateRepository = new ProductWorkRateRepository(_context);
        _workRateRepository = new WorkRateRepository(_context);
        _componentRepository = new ComponentRepository(_context);

        ProductCostCalculator calculator = new ProductCostCalculator(
            _bomLineRepository,
            _componentPriceRepository,
            _productWorkRateRepository,
            _workRateRepository);

        _snapshotService = new ProductCostSnapshotService(
            new ProductCostSnapshotRepository(_context),
            calculator);

        _today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    [Fact]
    public async Task ComponentPriceChange_CapturesCostThatWasInForceBeforeTheChange()
    {
        Guid productId = Guid.NewGuid();
        await SeedProductAsync(productId);
        Component component = await SeedComponentAsync("Ткань", ComponentCategory.Material, pricePerUnit: 100m);
        await SeedBomLineAsync(productId, component.Id, quantity: 2m);

        // The baseline the user would see in the UI right now: 2 * 100 = 200.
        CostBreakdown beforeChange = await CalculateAsync(productId);
        beforeChange.TotalCost.Should().Be(200m);

        CreateComponentPriceCommandHandler handler = new CreateComponentPriceCommandHandler(
            _componentRepository,
            _componentPriceRepository,
            _bomLineRepository,
            _snapshotService);

        await handler.Handle(
            new CreateComponentPriceCommand
            {
                ComponentId = component.Id,
                PricePerUnit = 400m,
                ValidFrom = _today.AddDays(-1),
            },
            CancellationToken.None);

        await _context.SaveChangesAsync();

        CostBreakdown afterChange = await CalculateAsync(productId);
        afterChange.TotalCost.Should().Be(800m);

        List<ProductCostSnapshot> snapshots = await _context.ProductCostSnapshots
            .Where(snapshot => snapshot.ProductId == productId)
            .ToListAsync();

        snapshots.Should().ContainSingle();
        snapshots[0].TriggerReason.Should().Be("ComponentPriceChanged");

        // The snapshot must hold the price that was in force before the change, not the new one.
        snapshots[0].TotalCost.Should().Be(200m);
        snapshots[0].MaterialCost.Should().Be(200m);
    }

    [Fact]
    public async Task ComponentUsedByFiveProducts_CreatesOneSnapshotPerProduct()
    {
        Component component = await SeedComponentAsync("Ткань", ComponentCategory.Material, pricePerUnit: 100m);
        List<Guid> productIds = new List<Guid>();

        for (int i = 0; i < 5; i++)
        {
            Guid productId = Guid.NewGuid();
            productIds.Add(productId);
            await SeedProductAsync(productId);
            await SeedBomLineAsync(productId, component.Id, quantity: 1m);
        }

        CreateComponentPriceCommandHandler handler = new CreateComponentPriceCommandHandler(
            _componentRepository,
            _componentPriceRepository,
            _bomLineRepository,
            _snapshotService);

        await handler.Handle(
            new CreateComponentPriceCommand
            {
                ComponentId = component.Id,
                PricePerUnit = 200m,
                ValidFrom = _today.AddDays(-1),
            },
            CancellationToken.None);

        await _context.SaveChangesAsync();

        List<ProductCostSnapshot> snapshots = await _context.ProductCostSnapshots.ToListAsync();

        // One snapshot per product, not a single shared row.
        snapshots.Should().HaveCount(5);
        snapshots.Select(snapshot => snapshot.ProductId)
            .Should()
            .BeEquivalentTo(productIds);
        snapshots.Should().OnlyContain(snapshot =>
            snapshot.TotalCost == 100m
            && snapshot.TriggerReason == "ComponentPriceChanged");
    }

    [Fact]
    public async Task TwoChangesOfTheSameInputOnTheSameDay_DoNotCreateDuplicateSnapshots()
    {
        Guid productId = Guid.NewGuid();
        await SeedProductAsync(productId);
        Component component = await SeedComponentAsync("Ткань", ComponentCategory.Material, pricePerUnit: 100m);
        await SeedBomLineAsync(productId, component.Id, quantity: 1m);

        CreateComponentPriceCommandHandler handler = new CreateComponentPriceCommandHandler(
            _componentRepository,
            _componentPriceRepository,
            _bomLineRepository,
            _snapshotService);

        await handler.Handle(
            new CreateComponentPriceCommand
            {
                ComponentId = component.Id,
                PricePerUnit = 150m,
                ValidFrom = _today.AddDays(-1),
            },
            CancellationToken.None);

        await _context.SaveChangesAsync();

        await handler.Handle(
            new CreateComponentPriceCommand
            {
                ComponentId = component.Id,
                PricePerUnit = 175m,
                ValidFrom = _today.AddDays(-1),
            },
            CancellationToken.None);

        await _context.SaveChangesAsync();

        List<ProductCostSnapshot> snapshots = await _context.ProductCostSnapshots
            .Where(snapshot => snapshot.ProductId == productId)
            .ToListAsync();

        // The idempotency rule from Этап 4 has to survive the new automatic trigger: the first
        // change of the day captures the state in force, the second one adds nothing.
        snapshots.Should().ContainSingle();
        snapshots[0].TotalCost.Should().Be(100m);
    }

    [Fact]
    public async Task BomChange_CapturesCompositionThatWasInForceBeforeTheChange()
    {
        Guid productId = Guid.NewGuid();
        await SeedProductAsync(productId);
        Component component = await SeedComponentAsync("Ткань", ComponentCategory.Material, pricePerUnit: 100m);
        await SeedBomLineAsync(productId, component.Id, quantity: 2m);

        // The original line has to be visible to the calculator before the handler replaces it.
        (await CalculateAsync(productId)).TotalCost.Should().Be(200m);

        CreateBomLineCommandHandler handler = new CreateBomLineCommandHandler(
            _bomLineRepository,
            _componentRepository,
            _snapshotService);

        await handler.Handle(
            new CreateBomLineCommand
            {
                ProductId = productId,
                ComponentId = component.Id,
                Quantity = 5m,
                ValidFrom = _today,
            },
            CancellationToken.None);

        await _context.SaveChangesAsync();

        List<ProductCostSnapshot> snapshots = await _context.ProductCostSnapshots
            .Where(snapshot => snapshot.ProductId == productId)
            .ToListAsync();

        // 2 * 100 from the composition being replaced, not 5 * 100 from the new one.
        snapshots.Should().ContainSingle();
        snapshots[0].TriggerReason.Should().Be("BomChanged");
        snapshots[0].TotalCost.Should().Be(200m);
    }

    [Fact]
    public async Task ComponentPriceChange_IgnoresProductsWhoseBomLineIsInactive()
    {
        Component component = await SeedComponentAsync("Ткань", ComponentCategory.Material, pricePerUnit: 100m);

        Guid liveProductId = Guid.NewGuid();
        await SeedProductAsync(liveProductId);
        await SeedBomLineAsync(liveProductId, component.Id, quantity: 1m);

        Guid archivedProductId = Guid.NewGuid();
        await SeedProductAsync(archivedProductId);
        await SeedBomLineAsync(archivedProductId, component.Id, quantity: 1m, isActive: false);

        IReadOnlyList<Guid> affectedProductIds = await _bomLineRepository
            .GetProductIdsByComponentIdAsync(component.Id, CancellationToken.None);

        affectedProductIds.Should().ContainSingle();
        affectedProductIds[0].Should().Be(liveProductId);

        CreateComponentPriceCommandHandler handler = new CreateComponentPriceCommandHandler(
            _componentRepository,
            _componentPriceRepository,
            _bomLineRepository,
            _snapshotService);

        await handler.Handle(
            new CreateComponentPriceCommand
            {
                ComponentId = component.Id,
                PricePerUnit = 300m,
                ValidFrom = _today.AddDays(-1),
            },
            CancellationToken.None);

        await _context.SaveChangesAsync();

        List<ProductCostSnapshot> snapshots = await _context.ProductCostSnapshots.ToListAsync();

        // The inactive line belongs to an archived composition and must not drag that product in.
        snapshots.Should().ContainSingle();
        snapshots[0].ProductId.Should().Be(liveProductId);
    }

    [Fact]
    public async Task WorkRateChange_IgnoresProductsWhoseProductWorkRateIsInactive()
    {
        Guid workRateId = Guid.NewGuid();

        _context.WorkRates.Add(new WorkRate
        {
            Id = workRateId,
            Name = "Базовая ставка",
            DailyWage = 2000m,
            ValidFrom = _today.AddDays(-30),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        Guid liveProductId = Guid.NewGuid();
        Guid archivedProductId = Guid.NewGuid();

        _context.ProductWorkRates.Add(new ProductWorkRate
        {
            Id = Guid.NewGuid(),
            ProductId = liveProductId,
            WorkRateId = workRateId,
            AssemblyRatePerDay = 8,
            ValidFrom = _today.AddDays(-30),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        _context.ProductWorkRates.Add(new ProductWorkRate
        {
            Id = Guid.NewGuid(),
            ProductId = archivedProductId,
            WorkRateId = workRateId,
            AssemblyRatePerDay = 8,
            ValidFrom = _today.AddDays(-30),
            IsActive = false,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await _context.SaveChangesAsync();

        IReadOnlyList<Guid> affectedProductIds = await _productWorkRateRepository
            .GetProductIdsByWorkRateIdAsync(workRateId, CancellationToken.None);

        affectedProductIds.Should().ContainSingle();
        affectedProductIds[0].Should().Be(liveProductId);
    }

    /// <summary>
    /// The case reported from production: the user adds a component, saves, then removes it again
    /// and saves. The composition table drops the row, so the cost has to drop with it. Before the
    /// interval bound existed the closed line kept winning the "latest version on the date" lookup,
    /// which left the material and total figures permanently inflated.
    /// </summary>
    [Fact]
    public async Task ComponentAddedThenRemoved_RestoresOriginalCosts()
    {
        Guid productId = Guid.NewGuid();
        await SeedProductAsync(productId);

        Guid workRateId = Guid.NewGuid();
        _context.WorkRates.Add(new WorkRate
        {
            Id = workRateId,
            Name = "Базовая ставка",
            DailyWage = 800m,
            ValidFrom = _today.AddDays(-30),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        _context.ProductWorkRates.Add(new ProductWorkRate
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            WorkRateId = workRateId,
            AssemblyRatePerDay = 8,
            ValidFrom = _today.AddDays(-30),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        Component original = await SeedComponentAsync("Ткань", ComponentCategory.Material, pricePerUnit: 100m);
        Component added = await SeedComponentAsync("Фурнитура", ComponentCategory.Material, pricePerUnit: 250m);
        await SeedBomLineAsync(productId, original.Id, quantity: 2m);

        UpsertProductBomCommandHandler handler = new UpsertProductBomCommandHandler(
            _bomLineRepository,
            _componentRepository,
            _snapshotService);

        CostBreakdown originalBreakdown = await CalculateAsync(productId);
        originalBreakdown.MaterialCost.Should().Be(200m);

        // Step one: the user adds the component and saves.
        await handler.Handle(
            new UpsertProductBomCommand
            {
                ProductId = productId,
                Lines = new List<UpsertProductBomItem>
                {
                    new UpsertProductBomItem { ComponentId = original.Id, Quantity = 2m },
                    new UpsertProductBomItem { ComponentId = added.Id, Quantity = 1m },
                },
            },
            CancellationToken.None);

        await _context.SaveChangesAsync();

        CostBreakdown afterAdd = await CalculateAsync(productId);
        afterAdd.MaterialCost.Should().Be(450m);
        afterAdd.TotalCost.Should().Be(550m);

        // Step two: ten minutes later the user realises the mistake and removes the component.
        await handler.Handle(
            new UpsertProductBomCommand
            {
                ProductId = productId,
                Lines = new List<UpsertProductBomItem>
                {
                    new UpsertProductBomItem { ComponentId = original.Id, Quantity = 2m },
                },
            },
            CancellationToken.None);

        await _context.SaveChangesAsync();

        CostBreakdown afterRemoval = await CalculateAsync(productId);

        // Both saves happen on the same date, so the removed component's version is closed at the
        // only boundary that can separate them: the removal drops it from this date's calculation.
        afterRemoval.MaterialCost.Should().Be(200m);
        afterRemoval.TotalCost.Should().Be(300m);

        IReadOnlyList<BomLine> composition = await _bomLineRepository
            .GetActiveByProductIdAsync(productId, CancellationToken.None);

        composition.Should().ContainSingle();
        composition[0].ComponentId.Should().Be(original.Id);
    }

    private async Task<CostBreakdown> CalculateAsync(Guid productId)
    {
        ProductCostCalculator calculator = new ProductCostCalculator(
            _bomLineRepository,
            _componentPriceRepository,
            _productWorkRateRepository,
            _workRateRepository);

        return await calculator.CalculateAsync(productId, _today, CancellationToken.None);
    }

    /// <summary>
    /// Persists the product immediately. The calculator and the snapshot service read through
    /// LINQ queries, which see only rows that reached the store, so seeding without a commit
    /// silently produces an empty composition.
    /// </summary>
    private async Task<Product> SeedProductAsync(Guid productId)
    {
        Product product = new Product
        {
            Id = productId,
            Sku = "SKU-" + productId.ToString("N").Substring(0, 8),
            Name = "Товар " + productId.ToString("N").Substring(0, 8),
            Status = ProductStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _context.Products.Add(product);
        await _context.SaveChangesAsync();

        return product;
    }

    private async Task<Component> SeedComponentAsync(string name, ComponentCategory category, decimal pricePerUnit)
    {
        Component component = new Component
        {
            Id = Guid.NewGuid(),
            Name = name,
            Unit = Unit.Kg,
            Category = category,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _context.Components.Add(component);

        _context.ComponentPrices.Add(new ComponentPrice
        {
            Id = Guid.NewGuid(),
            ComponentId = component.Id,
            PricePerUnit = pricePerUnit,
            ValidFrom = _today.AddDays(-30),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await _context.SaveChangesAsync();

        return component;
    }

    private async Task SeedBomLineAsync(Guid productId, Guid componentId, decimal quantity, bool isActive = true)
    {
        _context.BomLines.Add(new BomLine
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            ComponentId = componentId,
            Quantity = quantity,
            ValidFrom = _today.AddDays(-30),
            IsActive = isActive,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await _context.SaveChangesAsync();
    }
}
