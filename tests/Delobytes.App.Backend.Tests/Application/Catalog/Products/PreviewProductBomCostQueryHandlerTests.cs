// PreviewProductBomCostQueryHandlerTests.cs

using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Application.Queries.Products.PreviewProductBomCost;
using Delobytes.App.Backend.Catalog.Application.Services.CostCalculation;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using Delobytes.App.Backend.Contracts.Errors;
using FluentAssertions;
using FluentAssertions.Specialized;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Catalog.Products;

/// <summary>
/// Tests for PreviewProductBomCostQueryHandler.
/// Validates that a draft is priced in one component lookup and that the response carries the
/// persisted composition as a baseline together with the signed difference between the two.
/// </summary>
public class PreviewProductBomCostQueryHandlerTests
{
    private readonly Mock<IProductRepository> _productRepositoryMock;
    private readonly Mock<IComponentRepository> _componentRepositoryMock;
    private readonly Mock<ICostCalculator> _costCalculatorMock;
    private readonly PreviewProductBomCostQueryHandler _handler;

    public PreviewProductBomCostQueryHandlerTests()
    {
        _productRepositoryMock = new Mock<IProductRepository>();
        _componentRepositoryMock = new Mock<IComponentRepository>();
        _costCalculatorMock = new Mock<ICostCalculator>();
        _handler = new PreviewProductBomCostQueryHandler(
            _productRepositoryMock.Object,
            _componentRepositoryMock.Object,
            _costCalculatorMock.Object);
    }

    [Fact]
    public async Task Handle_DraftDiffersFromPersistedComposition_ReturnsSignedDelta()
    {
        // Arrange
        Guid productId = Guid.NewGuid();
        DateOnly asOf = new DateOnly(2026, 3, 15);

        Component material = BuildComponent("Ткань", ComponentCategory.Material);
        Component logistics = BuildComponent("Доставка", ComponentCategory.Logistics);

        // Draft: material went up, labour stayed put, because labour is not derived from the composition.
        CostBreakdown previewBreakdown = BuildBreakdown(
            productId,
            asOf,
            materialCost: 130m,
            logisticsCost: 50m,
            packagingCost: 20m,
            laborCost: 30m,
            isComplete: true);

        // Persisted: the composition the editor was opened with.
        CostBreakdown baselineBreakdown = BuildBreakdown(
            productId,
            asOf,
            materialCost: 100m,
            logisticsCost: 60m,
            packagingCost: 20m,
            laborCost: 30m,
            isComplete: true);

        SetupProductAndComponents(productId, asOf, new[] { material, logistics }, previewBreakdown, baselineBreakdown);

        PreviewProductBomCostQuery query = new PreviewProductBomCostQuery
        {
            ProductId = productId,
            AsOf = asOf,
            Lines = new List<PreviewProductBomCostLine>
            {
                new PreviewProductBomCostLine { ComponentId = material.Id, Quantity = 2m },
                new PreviewProductBomCostLine { ComponentId = logistics.Id, Quantity = 1m },
            },
        };

        // Act
        PreviewProductBomCostResponse response = await _handler.Handle(query, CancellationToken.None);

        // Assert
        response.Found.Should().BeTrue();
        response.Preview.Should().NotBeNull();
        response.Preview!.TotalCost.Should().Be(230m);

        response.Delta.Should().NotBeNull();
        response.Delta!.MaterialDelta.Should().Be(30m);
        response.Delta.LogisticsDelta.Should().Be(-10m);
        response.Delta.PackagingDelta.Should().Be(0m);
        response.Delta.LaborDelta.Should().Be(0m);
        response.Delta.TotalDelta.Should().Be(20m);
        response.Delta.TotalDelta.Should().Be(response.Preview.TotalCost - response.Baseline!.TotalCost);

        // Both sides are priced from one request, so the baseline must have been asked for exactly once.
        _costCalculatorMock.Verify(
            calculator => calculator.CalculateAsync(productId, asOf, It.IsAny<CancellationToken>()),
            Times.Once);
        _costCalculatorMock.Verify(
            calculator => calculator.CalculateForLinesAsync(
                productId,
                asOf,
                It.IsAny<IReadOnlyList<CostCalculationLine>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_BaselineMirrorsPersistedCostAndCarriesNoLines()
    {
        // Arrange
        Guid productId = Guid.NewGuid();
        DateOnly asOf = new DateOnly(2026, 3, 15);

        Component material = BuildComponent("Ткань", ComponentCategory.Material);

        CostBreakdown previewBreakdown = BuildBreakdown(productId, asOf, 100m, 0m, 0m, 30m, isComplete: true);
        CostBreakdown baselineBreakdown = BuildBreakdown(productId, asOf, 100m, 50m, 20m, 30m, isComplete: false);

        SetupProductAndComponents(productId, asOf, new[] { material }, previewBreakdown, baselineBreakdown);

        PreviewProductBomCostQuery query = new PreviewProductBomCostQuery
        {
            ProductId = productId,
            AsOf = asOf,
            Lines = new List<PreviewProductBomCostLine>
            {
                new PreviewProductBomCostLine { ComponentId = material.Id, Quantity = 2m },
            },
        };

        // Act
        PreviewProductBomCostResponse response = await _handler.Handle(query, CancellationToken.None);

        // Assert
        response.Baseline.Should().NotBeNull();
        response.Baseline!.MaterialCost.Should().Be(100m);
        response.Baseline.LogisticsCost.Should().Be(50m);
        response.Baseline.PackagingCost.Should().Be(20m);
        response.Baseline.LaborCost.Should().Be(30m);
        response.Baseline.TotalCost.Should().Be(200m);
        response.Baseline.IsComplete.Should().BeFalse();

        // The baseline type has no Lines member at all: assert that so a future refactor that adds one
        // cannot silently start shipping the whole composition on every keystroke.
        typeof(PreviewProductBomCostBaseline)
            .GetProperty("Lines")
            .Should()
            .BeNull();
    }

    [Fact]
    public async Task Handle_QuantityChangedInDraft_ChangesMaterialDeltaAndLeavesLaborDeltaAtZero()
    {
        // Arrange
        Guid productId = Guid.NewGuid();
        DateOnly asOf = new DateOnly(2026, 3, 15);

        Component material = BuildComponent("Ткань", ComponentCategory.Material);

        // The draft doubles the quantity of a component priced at 50 per unit.
        CostBreakdown previewBreakdown = BuildBreakdown(productId, asOf, 200m, 0m, 0m, 42m, isComplete: true);
        CostBreakdown baselineBreakdown = BuildBreakdown(productId, asOf, 100m, 0m, 0m, 42m, isComplete: true);

        SetupProductAndComponents(productId, asOf, new[] { material }, previewBreakdown, baselineBreakdown);

        PreviewProductBomCostQuery query = new PreviewProductBomCostQuery
        {
            ProductId = productId,
            AsOf = asOf,
            Lines = new List<PreviewProductBomCostLine>
            {
                new PreviewProductBomCostLine { ComponentId = material.Id, Quantity = 4m },
            },
        };

        // Act
        PreviewProductBomCostResponse response = await _handler.Handle(query, CancellationToken.None);

        // Assert
        response.Delta!.MaterialDelta.Should().Be(100m);
        response.Delta.LaborDelta.Should().Be(0m);
        response.Delta.TotalDelta.Should().Be(100m);
    }

    [Fact]
    public async Task Handle_DraftWithDuplicateComponentIds_ThrowsValidationFailed()
    {
        // Arrange
        Guid productId = Guid.NewGuid();
        Guid componentId = Guid.NewGuid();

        SetupProduct(productId);

        PreviewProductBomCostQuery query = new PreviewProductBomCostQuery
        {
            ProductId = productId,
            AsOf = new DateOnly(2026, 3, 15),
            Lines = new List<PreviewProductBomCostLine>
            {
                new PreviewProductBomCostLine { ComponentId = componentId, Quantity = 1m },
                new PreviewProductBomCostLine { ComponentId = componentId, Quantity = 2m },
            },
        };

        // Act
        Func<Task> act = async () => await _handler.Handle(query, CancellationToken.None);

        // Assert
        ExceptionAssertions<AppException> exception =
            await act.Should().ThrowAsync<AppException>();

        exception.Which.Code.Should().Be(ErrorCodes.Common.ValidationFailed);

        _componentRepositoryMock.Verify(
            repository => repository.GetByIdsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_DraftWithNonPositiveQuantity_ThrowsInvalidQuantity()
    {
        // Arrange
        Guid productId = Guid.NewGuid();
        Guid componentId = Guid.NewGuid();

        SetupProduct(productId);

        PreviewProductBomCostQuery query = new PreviewProductBomCostQuery
        {
            ProductId = productId,
            AsOf = new DateOnly(2026, 3, 15),
            Lines = new List<PreviewProductBomCostLine>
            {
                new PreviewProductBomCostLine { ComponentId = componentId, Quantity = 0m },
            },
        };

        // Act
        Func<Task> act = async () => await _handler.Handle(query, CancellationToken.None);

        // Assert
        ExceptionAssertions<AppException> exception =
            await act.Should().ThrowAsync<AppException>();

        exception.Which.Code.Should().Be(ErrorCodes.Catalog.BomLineInvalidQuantity);
    }

    [Fact]
    public async Task Handle_UnknownComponentId_ThrowsComponentNotFound()
    {
        // Arrange
        Guid productId = Guid.NewGuid();
        DateOnly asOf = new DateOnly(2026, 3, 15);
        Guid knownComponentId = Guid.NewGuid();
        Guid unknownComponentId = Guid.NewGuid();

        Component knownComponent = BuildComponent("Ткань", ComponentCategory.Material);
        knownComponent.Id = knownComponentId;

        SetupProduct(productId);

        _componentRepositoryMock
            .Setup(repository => repository.GetByIdsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Component> { knownComponent });

        PreviewProductBomCostQuery query = new PreviewProductBomCostQuery
        {
            ProductId = productId,
            AsOf = asOf,
            Lines = new List<PreviewProductBomCostLine>
            {
                new PreviewProductBomCostLine { ComponentId = knownComponentId, Quantity = 1m },
                new PreviewProductBomCostLine { ComponentId = unknownComponentId, Quantity = 1m },
            },
        };

        // Act
        Func<Task> act = async () => await _handler.Handle(query, CancellationToken.None);

        // Assert
        ExceptionAssertions<AppException> exception =
            await act.Should().ThrowAsync<AppException>();

        exception.Which.Code.Should().Be(ErrorCodes.Catalog.BomComponentNotFound);

        _costCalculatorMock.Verify(
            calculator => calculator.CalculateForLinesAsync(
                It.IsAny<Guid>(),
                It.IsAny<DateOnly>(),
                It.IsAny<IReadOnlyList<CostCalculationLine>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_EmptyDraft_ReturnsMissingBomWarningInsteadOfFailing()
    {
        // Arrange
        Guid productId = Guid.NewGuid();
        DateOnly asOf = new DateOnly(2026, 3, 15);

        CostWarning missingBomWarning = new CostWarning(
            CostWarningType.MissingBom,
            "Состав товара не определён на дату 2026-03-15.",
            null);

        // An empty draft is valid input: it means "no composition", so labour is still reported on its own.
        CostBreakdown previewBreakdown = new CostBreakdown(
            ProductId: productId,
            AsOfDate: asOf,
            MaterialCost: 0m,
            LogisticsCost: 0m,
            PackagingCost: 0m,
            LaborCost: 30m,
            TotalCost: 30m,
            Lines: new List<CostLine>(),
            Warnings: new List<CostWarning> { missingBomWarning });

        CostBreakdown baselineBreakdown = BuildBreakdown(productId, asOf, 100m, 0m, 0m, 30m, isComplete: true);

        SetupProduct(productId);

        _componentRepositoryMock
            .Setup(repository => repository.GetByIdsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Component>());

        _costCalculatorMock
            .Setup(calculator => calculator.CalculateForLinesAsync(
                productId,
                asOf,
                It.IsAny<IReadOnlyList<CostCalculationLine>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(previewBreakdown);

        _costCalculatorMock
            .Setup(calculator => calculator.CalculateAsync(productId, asOf, It.IsAny<CancellationToken>()))
            .ReturnsAsync(baselineBreakdown);

        PreviewProductBomCostQuery query = new PreviewProductBomCostQuery
        {
            ProductId = productId,
            AsOf = asOf,
            Lines = new List<PreviewProductBomCostLine>(),
        };

        // Act
        PreviewProductBomCostResponse response = await _handler.Handle(query, CancellationToken.None);

        // Assert
        response.Found.Should().BeTrue();
        response.Preview.Should().NotBeNull();
        response.Preview!.IsComplete.Should().BeFalse();
        response.Preview.Warnings.Should().ContainSingle();
        response.Preview.Warnings[0].Type.Should().Be(nameof(CostWarningType.MissingBom));
        response.Preview.LaborCost.Should().Be(30m);

        // The draft drops the only priced positions, so the material bucket loses all of it.
        // Labour is charged whether or not a composition exists, so it cancels out of the difference.
        response.Delta!.MaterialDelta.Should().Be(-100m);
        response.Delta.LaborDelta.Should().Be(0m);
        response.Delta.TotalDelta.Should().Be(-100m);

        _componentRepositoryMock.Verify(
            repository => repository.GetByIdsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_ProductNotFound_ReturnsFoundFalseWithoutCalculating()
    {
        // Arrange
        Guid productId = Guid.NewGuid();

        _productRepositoryMock
            .Setup(repository => repository.GetByIdAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Product?)null);

        PreviewProductBomCostQuery query = new PreviewProductBomCostQuery
        {
            ProductId = productId,
            AsOf = new DateOnly(2026, 3, 15),
            Lines = new List<PreviewProductBomCostLine>(),
        };

        // Act
        PreviewProductBomCostResponse response = await _handler.Handle(query, CancellationToken.None);

        // Assert
        response.Found.Should().BeFalse();
        response.Preview.Should().BeNull();
        response.Baseline.Should().BeNull();
        response.Delta.Should().BeNull();

        _costCalculatorMock.Verify(
            calculator => calculator.CalculateAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_NullAsOf_UsesTodayForBothSides()
    {
        // Arrange
        Guid productId = Guid.NewGuid();
        DateOnly today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date);
        Guid componentId = Guid.NewGuid();

        Component component = BuildComponent("Ткань", ComponentCategory.Material);
        component.Id = componentId;

        SetupProduct(productId);

        _componentRepositoryMock
            .Setup(repository => repository.GetByIdsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Component> { component });

        _costCalculatorMock
            .Setup(calculator => calculator.CalculateForLinesAsync(
                productId,
                today,
                It.IsAny<IReadOnlyList<CostCalculationLine>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildBreakdown(productId, today, 100m, 0m, 0m, 30m, isComplete: true));

        _costCalculatorMock
            .Setup(calculator => calculator.CalculateAsync(productId, today, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildBreakdown(productId, today, 40m, 0m, 0m, 30m, isComplete: true));

        PreviewProductBomCostQuery query = new PreviewProductBomCostQuery
        {
            ProductId = productId,
            Lines = new List<PreviewProductBomCostLine>
            {
                new PreviewProductBomCostLine { ComponentId = componentId, Quantity = 2m },
            },
        };

        // Act
        PreviewProductBomCostResponse response = await _handler.Handle(query, CancellationToken.None);

        // Assert
        response.Preview!.AsOfDate.Should().Be(today);
        response.Delta!.MaterialDelta.Should().Be(60m);
    }

    [Fact]
    public async Task Handle_MultipleLines_LoadsComponentsInOneBatchAndKeepsDraftOrder()
    {
        // Arrange
        Guid productId = Guid.NewGuid();
        DateOnly asOf = new DateOnly(2026, 3, 15);
        Guid firstId = Guid.NewGuid();
        Guid secondId = Guid.NewGuid();
        Guid thirdId = Guid.NewGuid();

        Component first = BuildComponent("Первая", ComponentCategory.Material);
        first.Id = firstId;
        Component second = BuildComponent("Вторая", ComponentCategory.Logistics);
        second.Id = secondId;
        Component third = BuildComponent("Третья", ComponentCategory.Packaging);
        third.Id = thirdId;

        IReadOnlyList<Guid>? requestedIds = null;

        SetupProduct(productId);

        // The repository is free to answer in any order; the handler must not rely on it.
        _componentRepositoryMock
            .Setup(repository => repository.GetByIdsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<Guid>, CancellationToken>((ids, _) => requestedIds = ids)
            .ReturnsAsync(new List<Component> { third, first, second });

        List<CostCalculationLine>? calculatorLines = null;

        _costCalculatorMock
            .Setup(calculator => calculator.CalculateForLinesAsync(
                productId,
                asOf,
                It.IsAny<IReadOnlyList<CostCalculationLine>>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, DateOnly, IReadOnlyList<CostCalculationLine>, CancellationToken>(
                (_, _, lines, _) => calculatorLines = lines.ToList())
            .ReturnsAsync(BuildBreakdown(productId, asOf, 10m, 0m, 0m, 0m, isComplete: true));

        _costCalculatorMock
            .Setup(calculator => calculator.CalculateAsync(productId, asOf, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildBreakdown(productId, asOf, 0m, 0m, 0m, 0m, isComplete: true));

        PreviewProductBomCostQuery query = new PreviewProductBomCostQuery
        {
            ProductId = productId,
            AsOf = asOf,
            Lines = new List<PreviewProductBomCostLine>
            {
                new PreviewProductBomCostLine { ComponentId = firstId, Quantity = 1m },
                new PreviewProductBomCostLine { ComponentId = secondId, Quantity = 2m },
                new PreviewProductBomCostLine { ComponentId = thirdId, Quantity = 3m },
            },
        };

        // Act
        await _handler.Handle(query, CancellationToken.None);

        // Assert
        requestedIds.Should().NotBeNull();
        requestedIds.Should().Equal(firstId, secondId, thirdId);

        _componentRepositoryMock.Verify(
            repository => repository.GetByIdsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()),
            Times.Once);

        calculatorLines.Should().NotBeNull();
        calculatorLines!.Select(line => line.ComponentId).Should().Equal(firstId, secondId, thirdId);
        calculatorLines.Select(line => line.Quantity).Should().Equal(1m, 2m, 3m);
    }

    [Fact]
    public async Task Handle_DraftWithMixedQuantities_ProducesBucketDeltasThatDoNotCancelOut()
    {
        // Arrange
        Guid productId = Guid.NewGuid();
        DateOnly asOf = new DateOnly(2026, 3, 15);
        Guid materialId = Guid.NewGuid();
        Guid logisticsId = Guid.NewGuid();

        Component material = BuildComponent("Ткань", ComponentCategory.Material);
        material.Id = materialId;
        Component logistics = BuildComponent("Доставка", ComponentCategory.Logistics);
        logistics.Id = logisticsId;

        // A material line for 200 is dropped and a logistics line for 200 appears: the total is unchanged,
        // so only the category-level deltas can tell the user the composition actually moved.
        CostBreakdown previewBreakdown = BuildBreakdown(productId, asOf, 0m, 200m, 0m, 30m, isComplete: true);
        CostBreakdown baselineBreakdown = BuildBreakdown(productId, asOf, 200m, 0m, 0m, 30m, isComplete: true);

        SetupProductAndComponents(productId, asOf, new[] { material, logistics }, previewBreakdown, baselineBreakdown);

        PreviewProductBomCostQuery query = new PreviewProductBomCostQuery
        {
            ProductId = productId,
            AsOf = asOf,
            Lines = new List<PreviewProductBomCostLine>
            {
                new PreviewProductBomCostLine { ComponentId = logisticsId, Quantity = 4m },
            },
        };

        // Act
        PreviewProductBomCostResponse response = await _handler.Handle(query, CancellationToken.None);

        // Assert
        response.Delta!.TotalDelta.Should().Be(0m);
        response.Delta.MaterialDelta.Should().Be(-200m);
        response.Delta.LogisticsDelta.Should().Be(200m);
    }

    private void SetupProduct(Guid productId)
    {
        Product product = new Product
        {
            Id = productId,
            Sku = "TEST-001",
            Name = "Test Product",
        };

        _productRepositoryMock
            .Setup(repository => repository.GetByIdAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);
    }

    private void SetupProductAndComponents(
        Guid productId,
        DateOnly asOf,
        IReadOnlyList<Component> components,
        CostBreakdown previewBreakdown,
        CostBreakdown baselineBreakdown)
    {
        SetupProduct(productId);

        _componentRepositoryMock
            .Setup(repository => repository.GetByIdsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(components);

        _costCalculatorMock
            .Setup(calculator => calculator.CalculateForLinesAsync(
                productId,
                asOf,
                It.IsAny<IReadOnlyList<CostCalculationLine>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(previewBreakdown);

        _costCalculatorMock
            .Setup(calculator => calculator.CalculateAsync(productId, asOf, It.IsAny<CancellationToken>()))
            .ReturnsAsync(baselineBreakdown);
    }

    private static Component BuildComponent(string name, ComponentCategory category)
    {
        return new Component
        {
            Id = Guid.NewGuid(),
            Name = name,
            Unit = Unit.Piece,
            Category = category,
            IsActive = true,
        };
    }

    private static CostBreakdown BuildBreakdown(
        Guid productId,
        DateOnly asOf,
        decimal materialCost,
        decimal logisticsCost,
        decimal packagingCost,
        decimal laborCost,
        bool isComplete)
    {
        IReadOnlyList<CostWarning> warnings = isComplete
            ? new List<CostWarning>()
            : new List<CostWarning>
            {
                new CostWarning(CostWarningType.MissingComponentPrice, "Нет цены на компонент.", Guid.NewGuid()),
            };

        return new CostBreakdown(
            ProductId: productId,
            AsOfDate: asOf,
            MaterialCost: materialCost,
            LogisticsCost: logisticsCost,
            PackagingCost: packagingCost,
            LaborCost: laborCost,
            TotalCost: materialCost + logisticsCost + packagingCost + laborCost,
            Lines: new List<CostLine>(),
            Warnings: warnings);
    }
}
