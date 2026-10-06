using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Application.Services.CostCalculation;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using FluentAssertions;
using Moq;

namespace Delobytes.App.Backend.Tests.Application.Catalog.CostCalculation;

public class ProductCostCalculatorTests
{
    private const decimal Tolerance = 0.0001m;

    private readonly Mock<IBomLineRepository> _bomLineRepositoryMock;
    private readonly Mock<IComponentPriceRepository> _componentPriceRepositoryMock;
    private readonly Mock<IProductWorkRateRepository> _productWorkRateRepositoryMock;
    private readonly Mock<IWorkRateVersionRepository> _workRateVersionRepositoryMock;
    private readonly ProductCostCalculator _calculator;

    public ProductCostCalculatorTests()
    {
        _bomLineRepositoryMock = new Mock<IBomLineRepository>();
        _componentPriceRepositoryMock = new Mock<IComponentPriceRepository>();
        _productWorkRateRepositoryMock = new Mock<IProductWorkRateRepository>();
        _workRateVersionRepositoryMock = new Mock<IWorkRateVersionRepository>();

        _calculator = new ProductCostCalculator(
            _bomLineRepositoryMock.Object,
            _componentPriceRepositoryMock.Object,
            _productWorkRateRepositoryMock.Object,
            _workRateVersionRepositoryMock.Object);
    }

    [Fact]
    public async Task CalculateAsync_FullDataSet_SplitsCostByCategory()
    {
        Guid productId = Guid.NewGuid();
        DateOnly asOf = new DateOnly(2026, 3, 1);

        Component material = CreateComponent("Ткань", ComponentCategory.Material);
        Component logistics = CreateComponent("Доставка", ComponentCategory.Logistics);
        Component packaging = CreateComponent("Коробка", ComponentCategory.Packaging);

        SetupBom(productId, asOf, new[]
        {
            CreateBomLine(productId, material, 2m),
            CreateBomLine(productId, logistics, 1m),
            CreateBomLine(productId, packaging, 4m),
        });

        SetupPrices(asOf, new Dictionary<Guid, decimal>
        {
            { material.Id, 100m },
            { logistics.Id, 50m },
            { packaging.Id, 10m },
        });

        SetupLabor(productId, asOf, dailyWage: 2000m, assemblyRatePerDay: 8);

        CostBreakdown breakdown = await _calculator.CalculateAsync(productId, asOf, CancellationToken.None);

        // Material 2 * 100 = 200, logistics 1 * 50 = 50, packaging 4 * 10 = 40.
        breakdown.MaterialCost.Should().BeApproximately(200m, Tolerance);
        breakdown.LogisticsCost.Should().BeApproximately(50m, Tolerance);
        breakdown.PackagingCost.Should().BeApproximately(40m, Tolerance);

        // Labour 2000 / 8 = 250.
        breakdown.LaborCost.Should().BeApproximately(250m, Tolerance);
        breakdown.TotalCost.Should().BeApproximately(540m, Tolerance);

        breakdown.Lines.Should().HaveCount(3);
        breakdown.Warnings.Should().BeEmpty();
        breakdown.IsComplete.Should().BeTrue();
        breakdown.AsOfDate.Should().Be(asOf);
    }

    [Fact]
    public async Task CalculateAsync_MissingComponentPrice_WarnsAndKeepsOtherLines()
    {
        Guid productId = Guid.NewGuid();
        DateOnly asOf = new DateOnly(2026, 3, 1);

        Component priced = CreateComponent("Ткань", ComponentCategory.Material);
        Component unpriced = CreateComponent("Фурнитура", ComponentCategory.Material);

        SetupBom(productId, asOf, new[]
        {
            CreateBomLine(productId, priced, 2m),
            CreateBomLine(productId, unpriced, 3m),
        });

        SetupPrices(asOf, new Dictionary<Guid, decimal> { { priced.Id, 100m } });
        SetupLabor(productId, asOf, dailyWage: 1000m, assemblyRatePerDay: 10);

        CostBreakdown breakdown = await _calculator.CalculateAsync(productId, asOf, CancellationToken.None);

        breakdown.MaterialCost.Should().BeApproximately(200m, Tolerance);
        breakdown.TotalCost.Should().BeApproximately(300m, Tolerance);

        // The unpriced position stays in Lines with a zero price so the UI can highlight it.
        breakdown.Lines.Should().HaveCount(2);
        CostLine unpricedLine = breakdown.Lines.Single(line => line.ComponentId == unpriced.Id);
        unpricedLine.PricePerUnit.Should().Be(0m);
        unpricedLine.LineTotal.Should().Be(0m);

        breakdown.Warnings.Should().HaveCount(1);
        breakdown.Warnings[0].Type.Should().Be(CostWarningType.MissingComponentPrice);
        breakdown.Warnings[0].ComponentId.Should().Be(unpriced.Id);
        breakdown.IsComplete.Should().BeFalse();
    }

    [Fact]
    public async Task CalculateAsync_NoBom_ReturnsZerosWithMissingBomWarning()
    {
        Guid productId = Guid.NewGuid();
        DateOnly asOf = new DateOnly(2026, 3, 1);

        _bomLineRepositoryMock
            .Setup(repository => repository.GetEffectiveAtAsync(productId, asOf, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<BomLine>());

        _productWorkRateRepositoryMock
            .Setup(repository => repository.GetEffectiveAtAsync(productId, asOf, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProductWorkRate?)null);

        CostBreakdown breakdown = await _calculator.CalculateAsync(productId, asOf, CancellationToken.None);

        breakdown.MaterialCost.Should().Be(0m);
        breakdown.LogisticsCost.Should().Be(0m);
        breakdown.PackagingCost.Should().Be(0m);
        breakdown.LaborCost.Should().Be(0m);
        breakdown.TotalCost.Should().Be(0m);
        breakdown.Lines.Should().BeEmpty();

        // Missing composition and missing labour rate are independent observations, so a product
        // without both reports both instead of stopping at the first one.
        breakdown.Warnings.Should().HaveCount(2);
        breakdown.Warnings.Should().Contain(warning => warning.Type == CostWarningType.MissingBom);
        breakdown.Warnings.Should().Contain(warning => warning.Type == CostWarningType.MissingWorkRate);
        breakdown.IsComplete.Should().BeFalse();
    }

    [Fact]
    public async Task CalculateAsync_NoBom_StillCountsLabourCost()
    {
        Guid productId = Guid.NewGuid();
        DateOnly asOf = new DateOnly(2026, 3, 1);

        _bomLineRepositoryMock
            .Setup(repository => repository.GetEffectiveAtAsync(productId, asOf, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<BomLine>());

        SetupLabor(productId, asOf, dailyWage: 2500m, assemblyRatePerDay: 105);

        CostBreakdown breakdown = await _calculator.CalculateAsync(productId, asOf, CancellationToken.None);

        // Labour is a property of the assembly step, not of the composition, so an unfinished BOM
        // must not hide a cost that is already known: 2500 / 105.
        breakdown.MaterialCost.Should().Be(0m);
        breakdown.LogisticsCost.Should().Be(0m);
        breakdown.PackagingCost.Should().Be(0m);
        breakdown.LaborCost.Should().BeApproximately(2500m / 105m, Tolerance);
        breakdown.TotalCost.Should().BeApproximately(2500m / 105m, Tolerance);
        breakdown.Lines.Should().BeEmpty();
        breakdown.Warnings.Should().ContainSingle(warning => warning.Type == CostWarningType.MissingBom);
        breakdown.IsComplete.Should().BeFalse();
    }

    [Fact]
    public async Task CalculateAsync_MissingWorkRate_ReturnsMaterialsWithZeroLabor()
    {
        Guid productId = Guid.NewGuid();
        DateOnly asOf = new DateOnly(2026, 3, 1);

        Component material = CreateComponent("Ткань", ComponentCategory.Material);
        SetupBom(productId, asOf, new[] { CreateBomLine(productId, material, 2m) });
        SetupPrices(asOf, new Dictionary<Guid, decimal> { { material.Id, 100m } });

        _productWorkRateRepositoryMock
            .Setup(repository => repository.GetEffectiveAtAsync(productId, asOf, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProductWorkRate?)null);

        CostBreakdown breakdown = await _calculator.CalculateAsync(productId, asOf, CancellationToken.None);

        breakdown.MaterialCost.Should().BeApproximately(200m, Tolerance);
        breakdown.LaborCost.Should().Be(0m);
        breakdown.TotalCost.Should().BeApproximately(200m, Tolerance);
        breakdown.Warnings.Should().Contain(warning => warning.Type == CostWarningType.MissingWorkRate);
    }

    [Fact]
    public async Task CalculateAsync_ZeroAssemblyRate_WarnsInsteadOfThrowing()
    {
        Guid productId = Guid.NewGuid();
        DateOnly asOf = new DateOnly(2026, 3, 1);

        Component material = CreateComponent("Ткань", ComponentCategory.Material);
        SetupBom(productId, asOf, new[] { CreateBomLine(productId, material, 2m) });
        SetupPrices(asOf, new Dictionary<Guid, decimal> { { material.Id, 100m } });
        SetupLabor(productId, asOf, dailyWage: 2000m, assemblyRatePerDay: 0);

        CostBreakdown breakdown = await _calculator.CalculateAsync(productId, asOf, CancellationToken.None);

        breakdown.LaborCost.Should().Be(0m);
        breakdown.MaterialCost.Should().BeApproximately(200m, Tolerance);
        breakdown.Warnings.Should().Contain(warning => warning.Type == CostWarningType.InvalidQuantity);
    }

    [Fact]
    public async Task CalculateAsync_PastDate_UsesHistoricalPriceNotCurrentOne()
    {
        Guid productId = Guid.NewGuid();
        Component material = CreateComponent("Ткань", ComponentCategory.Material);

        DateOnly asOf = new DateOnly(2026, 1, 15);
        DateOnly laterDate = new DateOnly(2026, 3, 1);

        SetupBom(productId, asOf, new[] { CreateBomLine(productId, material, 10m) });

        // The repository contract resolves the version effective on the requested date; the current
        // price of 300 must not leak into a January calculation.
        _componentPriceRepositoryMock
            .Setup(repository => repository.GetEffectiveAtAsync(material.Id, asOf, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ComponentPrice { ComponentId = material.Id, PricePerUnit = 100m, ValidFrom = asOf });

        _componentPriceRepositoryMock
            .Setup(repository => repository.GetEffectiveAtAsync(material.Id, laterDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ComponentPrice { ComponentId = material.Id, PricePerUnit = 300m, ValidFrom = laterDate });

        SetupLabor(productId, asOf, dailyWage: 1000m, assemblyRatePerDay: 10);

        CostBreakdown breakdown = await _calculator.CalculateAsync(productId, asOf, CancellationToken.None);

        breakdown.MaterialCost.Should().BeApproximately(1000m, Tolerance);
        breakdown.TotalCost.Should().BeApproximately(1100m, Tolerance);
        breakdown.Lines.Single().PricePerUnit.Should().BeApproximately(100m, Tolerance);
    }

    [Fact]
    public async Task CalculateAsync_InvalidQuantity_WarnsButStillCountsLine()
    {
        Guid productId = Guid.NewGuid();
        DateOnly asOf = new DateOnly(2026, 3, 1);

        Component material = CreateComponent("Ткань", ComponentCategory.Material);
        Component invalid = CreateComponent("Клей", ComponentCategory.Material);

        SetupBom(productId, asOf, new[]
        {
            CreateBomLine(productId, material, 2m),
            CreateBomLine(productId, invalid, 0m),
        });

        SetupPrices(asOf, new Dictionary<Guid, decimal>
        {
            { material.Id, 100m },
            { invalid.Id, 50m },
        });

        SetupLabor(productId, asOf, dailyWage: 1000m, assemblyRatePerDay: 10);

        CostBreakdown breakdown = await _calculator.CalculateAsync(productId, asOf, CancellationToken.None);

        breakdown.Warnings.Should().Contain(warning =>
            warning.Type == CostWarningType.InvalidQuantity && warning.ComponentId == invalid.Id);
        breakdown.MaterialCost.Should().BeApproximately(200m, Tolerance);
    }

    [Fact]
    public async Task CalculateAsync_MissingWorkRateRowForReferencedRate_WarnsWithZeroLabor()
    {
        Guid productId = Guid.NewGuid();
        DateOnly asOf = new DateOnly(2026, 3, 1);

        Component material = CreateComponent("Ткань", ComponentCategory.Material);
        SetupBom(productId, asOf, new[] { CreateBomLine(productId, material, 1m) });
        SetupPrices(asOf, new Dictionary<Guid, decimal> { { material.Id, 100m } });

        Guid workRateId = Guid.NewGuid();

        _productWorkRateRepositoryMock
            .Setup(repository => repository.GetEffectiveAtAsync(productId, asOf, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProductWorkRate
            {
                ProductId = productId,
                WorkRateId = workRateId,
                AssemblyRatePerDay = 10,
                ValidFrom = asOf,
                IsActive = true,
            });

        // The referenced rate was removed physically; the calculation must survive it.
        _workRateVersionRepositoryMock
            .Setup(repository => repository.GetEffectiveAtAsync(workRateId, asOf, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkRateVersion?)null);

        CostBreakdown breakdown = await _calculator.CalculateAsync(productId, asOf, CancellationToken.None);

        breakdown.LaborCost.Should().Be(0m);
        breakdown.Warnings.Should().Contain(warning => warning.Type == CostWarningType.MissingWorkRate);
    }

    [Fact]
    public async Task CalculateAsync_AlwaysQueriesInputsForRequestedDate()
    {
        Guid productId = Guid.NewGuid();
        DateOnly asOf = new DateOnly(2025, 12, 31);

        _bomLineRepositoryMock
            .Setup(repository => repository.GetEffectiveAtAsync(productId, asOf, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<BomLine>());

        await _calculator.CalculateAsync(productId, asOf, CancellationToken.None);

        _bomLineRepositoryMock.Verify(
            repository => repository.GetEffectiveAtAsync(productId, asOf, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── Wage versions ────────────────────────────────────────────────────────────

    [Fact]
    public async Task CalculateAsync_WageVersionEffectiveAtAsOf_IsUsed()
    {
        // The wage is no longer a column on the work rate row: it is resolved through the version
        // that was in force on the requested date.
        Guid productId = Guid.NewGuid();
        DateOnly asOf = new DateOnly(2026, 3, 15);

        Component material = CreateComponent("Ткань", ComponentCategory.Material);
        SetupBom(productId, asOf, new[] { CreateBomLine(productId, material, 1m) });
        SetupPrices(asOf, new Dictionary<Guid, decimal> { { material.Id, 100m } });

        Guid workRateId = SetupProductWorkRate(productId, asOf, assemblyRatePerDay: 10);

        WorkRateVersion effective = new WorkRateVersion
        {
            Id = Guid.NewGuid(),
            WorkRateId = workRateId,
            DailyWage = 2000m,
            ValidFrom = new DateOnly(2026, 1, 1),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow.AddMonths(-2),
        };

        _workRateVersionRepositoryMock
            .Setup(repository => repository.GetEffectiveAtAsync(workRateId, asOf, It.IsAny<CancellationToken>()))
            .ReturnsAsync(effective);

        CostBreakdown breakdown = await _calculator.CalculateAsync(productId, asOf, CancellationToken.None);

        // Labour 2000 / 10 = 200, material 1 * 100 = 100.
        breakdown.MaterialCost.Should().BeApproximately(100m, Tolerance);
        breakdown.LaborCost.Should().BeApproximately(200m, Tolerance);
        breakdown.TotalCost.Should().BeApproximately(300m, Tolerance);
        breakdown.Warnings.Should().BeEmpty();

        // The wage must be asked for the calculation date, not for today.
        _workRateVersionRepositoryMock.Verify(
            repository => repository.GetEffectiveAtAsync(workRateId, asOf, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CalculateAsync_NoWageVersionEffectiveAtAsOf_YieldsZeroLaborWithMissingWorkRate()
    {
        // A wage introduced after the requested date means the product had no resolvable labour on
        // that date. The calculation reports it instead of falling back to the current wage.
        Guid productId = Guid.NewGuid();
        DateOnly asOf = new DateOnly(2026, 2, 1);

        Component material = CreateComponent("Ткань", ComponentCategory.Material);
        SetupBom(productId, asOf, new[] { CreateBomLine(productId, material, 2m) });
        SetupPrices(asOf, new Dictionary<Guid, decimal> { { material.Id, 100m } });

        Guid workRateId = SetupProductWorkRate(productId, asOf, assemblyRatePerDay: 8);

        _workRateVersionRepositoryMock
            .Setup(repository => repository.GetEffectiveAtAsync(workRateId, asOf, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkRateVersion?)null);

        CostBreakdown breakdown = await _calculator.CalculateAsync(productId, asOf, CancellationToken.None);

        breakdown.MaterialCost.Should().BeApproximately(200m, Tolerance);
        breakdown.LaborCost.Should().Be(0m);
        breakdown.TotalCost.Should().BeApproximately(200m, Tolerance);
        breakdown.Warnings.Should().Contain(warning => warning.Type == CostWarningType.MissingWorkRate);
        breakdown.IsComplete.Should().BeFalse();
    }

    [Fact]
    public async Task CalculateAsync_SupersededInactiveVersion_StillResolvesForAnEarlierDate()
    {
        // The repository contract ignores IsActive on purpose. The calculator must therefore not
        // filter the result out on its side: a version deactivated by a newer one is still the
        // correct wage for a date that falls before the replacement.
        Guid productId = Guid.NewGuid();
        DateOnly asOf = new DateOnly(2026, 1, 20);

        Component material = CreateComponent("Ткань", ComponentCategory.Material);
        SetupBom(productId, asOf, new[] { CreateBomLine(productId, material, 1m) });
        SetupPrices(asOf, new Dictionary<Guid, decimal> { { material.Id, 0m } });

        Guid workRateId = SetupProductWorkRate(productId, asOf, assemblyRatePerDay: 4);

        WorkRateVersion superseded = new WorkRateVersion
        {
            Id = Guid.NewGuid(),
            WorkRateId = workRateId,
            DailyWage = 1600m,
            ValidFrom = new DateOnly(2026, 1, 1),
            IsActive = false,
            UpdatedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow.AddMonths(-1),
        };

        _workRateVersionRepositoryMock
            .Setup(repository => repository.GetEffectiveAtAsync(workRateId, asOf, It.IsAny<CancellationToken>()))
            .ReturnsAsync(superseded);

        CostBreakdown breakdown = await _calculator.CalculateAsync(productId, asOf, CancellationToken.None);

        // Labour 1600 / 4 = 400, and no warning: the inactive flag on the version is irrelevant
        // for a historical date.
        breakdown.LaborCost.Should().BeApproximately(400m, Tolerance);
        breakdown.Warnings.Should().NotContain(warning => warning.Type == CostWarningType.MissingWorkRate);
    }

    private static Component CreateComponent(string name, ComponentCategory category)
    {
        return new Component
        {
            Id = Guid.NewGuid(),
            Name = name,
            Category = category,
            Unit = Unit.Piece,
            IsActive = true,
        };
    }

    private static BomLine CreateBomLine(Guid productId, Component component, decimal quantity)
    {
        return new BomLine
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            ComponentId = component.Id,
            Quantity = quantity,
            ValidFrom = new DateOnly(2026, 1, 1),
            IsActive = true,
            Component = component,
        };
    }

    private void SetupBom(Guid productId, DateOnly asOf, IReadOnlyList<BomLine> lines)
    {
        _bomLineRepositoryMock
            .Setup(repository => repository.GetEffectiveAtAsync(productId, asOf, It.IsAny<CancellationToken>()))
            .ReturnsAsync(lines);
    }

    private void SetupPrices(DateOnly asOf, IReadOnlyDictionary<Guid, decimal> pricesByComponentId)
    {
        foreach (KeyValuePair<Guid, decimal> pair in pricesByComponentId)
        {
            _componentPriceRepositoryMock
                .Setup(repository => repository.GetEffectiveAtAsync(pair.Key, asOf, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ComponentPrice
                {
                    ComponentId = pair.Key,
                    PricePerUnit = pair.Value,
                    ValidFrom = new DateOnly(2026, 1, 1),
                    IsActive = true,
                });
        }
    }

    /// <summary>
    /// Stubs the assembly output rate for the product and returns the work rate id it references,
    /// so the caller can stub the wage version lookup for the same id.
    /// </summary>
    private Guid SetupProductWorkRate(Guid productId, DateOnly asOf, int assemblyRatePerDay)
    {
        Guid workRateId = Guid.NewGuid();

        _productWorkRateRepositoryMock
            .Setup(repository => repository.GetEffectiveAtAsync(productId, asOf, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProductWorkRate
            {
                Id = Guid.NewGuid(),
                ProductId = productId,
                WorkRateId = workRateId,
                AssemblyRatePerDay = assemblyRatePerDay,
                ValidFrom = new DateOnly(2026, 1, 1),
                IsActive = true,
            });

        return workRateId;
    }

    private void SetupLabor(Guid productId, DateOnly asOf, decimal dailyWage, int assemblyRatePerDay)
    {
        Guid workRateId = Guid.NewGuid();

        _productWorkRateRepositoryMock
            .Setup(repository => repository.GetEffectiveAtAsync(productId, asOf, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProductWorkRate
            {
                ProductId = productId,
                WorkRateId = workRateId,
                AssemblyRatePerDay = assemblyRatePerDay,
                ValidFrom = new DateOnly(2026, 1, 1),
                IsActive = true,
            });

        _workRateVersionRepositoryMock
            .Setup(repository => repository.GetEffectiveAtAsync(workRateId, asOf, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkRateVersion
            {
                Id = Guid.NewGuid(),
                WorkRateId = workRateId,
                DailyWage = dailyWage,
                ValidFrom = new DateOnly(2026, 1, 1),
                IsActive = true,
            });
    }
}
