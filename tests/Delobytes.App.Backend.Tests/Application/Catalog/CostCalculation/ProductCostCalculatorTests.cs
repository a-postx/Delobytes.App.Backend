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
    private readonly Mock<IWorkRateRepository> _workRateRepositoryMock;
    private readonly ProductCostCalculator _calculator;

    public ProductCostCalculatorTests()
    {
        _bomLineRepositoryMock = new Mock<IBomLineRepository>();
        _componentPriceRepositoryMock = new Mock<IComponentPriceRepository>();
        _productWorkRateRepositoryMock = new Mock<IProductWorkRateRepository>();
        _workRateRepositoryMock = new Mock<IWorkRateRepository>();

        _calculator = new ProductCostCalculator(
            _bomLineRepositoryMock.Object,
            _componentPriceRepositoryMock.Object,
            _productWorkRateRepositoryMock.Object,
            _workRateRepositoryMock.Object);
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
        _workRateRepositoryMock
            .Setup(repository => repository.GetEffectiveAtAsync(workRateId, asOf, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkRate?)null);

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

        _workRateRepositoryMock
            .Setup(repository => repository.GetEffectiveAtAsync(workRateId, asOf, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkRate
            {
                Id = workRateId,
                Name = "Сборщик",
                DailyWage = dailyWage,
                ValidFrom = new DateOnly(2026, 1, 1),
                IsActive = true,
            });
    }
}
