// GetProductCostsBatchQueryHandlerTests.cs

using Delobytes.App.Backend.Catalog.Application.Queries.Products.GetProductCostsBatch;
using Delobytes.App.Backend.Catalog.Application.Services.CostCalculation;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using FluentAssertions;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Catalog.Products;

/// <summary>
/// Tests for GetProductCostsBatchQueryHandler.
/// Validates batch cost calculation and mapping to lightweight summary DTOs.
/// </summary>
public class GetProductCostsBatchQueryHandlerTests
{
    private readonly Mock<ICostCalculator> _costCalculatorMock;
    private readonly GetProductCostsBatchQueryHandler _handler;

    public GetProductCostsBatchQueryHandlerTests()
    {
        _costCalculatorMock = new Mock<ICostCalculator>();
        _handler = new GetProductCostsBatchQueryHandler(_costCalculatorMock.Object);
    }

    [Fact]
    public async Task Handle_EmptyProductIdList_ReturnsEmptyItems()
    {
        // Arrange
        DateOnly asOf = new DateOnly(2026, 3, 15);

        GetProductCostsBatchQuery query = new GetProductCostsBatchQuery
        {
            ProductIds = new List<Guid>(),
            AsOf = asOf,
        };

        // Act
        GetProductCostsBatchResponse response = await _handler.Handle(query, CancellationToken.None);

        // Assert
        response.AsOfDate.Should().Be(asOf);
        response.Items.Should().BeEmpty();

        _costCalculatorMock.Verify(
            calculator => calculator.CalculateAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_SingleProduct_ReturnsOneSummary()
    {
        // Arrange
        Guid productId = Guid.NewGuid();
        DateOnly asOf = new DateOnly(2026, 3, 15);

        CostBreakdown breakdown = new CostBreakdown(
            ProductId: productId,
            AsOfDate: asOf,
            MaterialCost: 100m,
            LogisticsCost: 50m,
            PackagingCost: 20m,
            LaborCost: 30m,
            TotalCost: 200m,
            Lines: new List<CostLine>(),
            Warnings: new List<CostWarning>());

        _costCalculatorMock
            .Setup(calculator => calculator.CalculateAsync(productId, asOf, It.IsAny<CancellationToken>()))
            .ReturnsAsync(breakdown);

        GetProductCostsBatchQuery query = new GetProductCostsBatchQuery
        {
            ProductIds = new List<Guid> { productId },
            AsOf = asOf,
        };

        // Act
        GetProductCostsBatchResponse response = await _handler.Handle(query, CancellationToken.None);

        // Assert
        response.AsOfDate.Should().Be(asOf);
        response.Items.Should().HaveCount(1);

        ProductCostSummaryDto summary = response.Items[0];
        summary.ProductId.Should().Be(productId);
        summary.MaterialCost.Should().Be(100m);
        summary.LogisticsCost.Should().Be(50m);
        summary.PackagingCost.Should().Be(20m);
        summary.LaborCost.Should().Be(30m);
        summary.TotalCost.Should().Be(200m);
        summary.IsComplete.Should().BeTrue();

        _costCalculatorMock.Verify(
            calculator => calculator.CalculateAsync(productId, asOf, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_MultipleProducts_ReturnsAllSummaries()
    {
        // Arrange
        Guid productId1 = Guid.NewGuid();
        Guid productId2 = Guid.NewGuid();
        Guid productId3 = Guid.NewGuid();
        DateOnly asOf = new DateOnly(2026, 3, 15);

        CostBreakdown breakdown1 = new CostBreakdown(
            ProductId: productId1,
            AsOfDate: asOf,
            MaterialCost: 100m,
            LogisticsCost: 50m,
            PackagingCost: 20m,
            LaborCost: 30m,
            TotalCost: 200m,
            Lines: new List<CostLine>(),
            Warnings: new List<CostWarning>());

        CostBreakdown breakdown2 = new CostBreakdown(
            ProductId: productId2,
            AsOfDate: asOf,
            MaterialCost: 200m,
            LogisticsCost: 100m,
            PackagingCost: 40m,
            LaborCost: 60m,
            TotalCost: 400m,
            Lines: new List<CostLine>(),
            Warnings: new List<CostWarning>());

        CostBreakdown breakdown3 = new CostBreakdown(
            ProductId: productId3,
            AsOfDate: asOf,
            MaterialCost: 75m,
            LogisticsCost: 25m,
            PackagingCost: 15m,
            LaborCost: 35m,
            TotalCost: 150m,
            Lines: new List<CostLine>(),
            Warnings: new List<CostWarning> { new CostWarning(CostWarningType.MissingWorkRate, "Missing rate", null) });

        _costCalculatorMock
            .Setup(calculator => calculator.CalculateAsync(productId1, asOf, It.IsAny<CancellationToken>()))
            .ReturnsAsync(breakdown1);

        _costCalculatorMock
            .Setup(calculator => calculator.CalculateAsync(productId2, asOf, It.IsAny<CancellationToken>()))
            .ReturnsAsync(breakdown2);

        _costCalculatorMock
            .Setup(calculator => calculator.CalculateAsync(productId3, asOf, It.IsAny<CancellationToken>()))
            .ReturnsAsync(breakdown3);

        GetProductCostsBatchQuery query = new GetProductCostsBatchQuery
        {
            ProductIds = new List<Guid> { productId1, productId2, productId3 },
            AsOf = asOf,
        };

        // Act
        GetProductCostsBatchResponse response = await _handler.Handle(query, CancellationToken.None);

        // Assert
        response.AsOfDate.Should().Be(asOf);
        response.Items.Should().HaveCount(3);

        ProductCostSummaryDto summary1 = response.Items[0];
        summary1.ProductId.Should().Be(productId1);
        summary1.TotalCost.Should().Be(200m);
        summary1.IsComplete.Should().BeTrue();

        ProductCostSummaryDto summary2 = response.Items[1];
        summary2.ProductId.Should().Be(productId2);
        summary2.TotalCost.Should().Be(400m);
        summary2.IsComplete.Should().BeTrue();

        ProductCostSummaryDto summary3 = response.Items[2];
        summary3.ProductId.Should().Be(productId3);
        summary3.TotalCost.Should().Be(150m);
        summary3.IsComplete.Should().BeFalse();

        _costCalculatorMock.Verify(
            calculator => calculator.CalculateAsync(It.IsAny<Guid>(), asOf, It.IsAny<CancellationToken>()),
            Times.Exactly(3));
    }

    [Fact]
    public async Task Handle_NullAsOf_UsesTodayForCalculation()
    {
        // Arrange
        Guid productId = Guid.NewGuid();
        DateOnly today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date);

        CostBreakdown breakdown = new CostBreakdown(
            ProductId: productId,
            AsOfDate: today,
            MaterialCost: 150m,
            LogisticsCost: 0m,
            PackagingCost: 0m,
            LaborCost: 50m,
            TotalCost: 200m,
            Lines: new List<CostLine>(),
            Warnings: new List<CostWarning>());

        _costCalculatorMock
            .Setup(calculator => calculator.CalculateAsync(productId, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(breakdown);

        GetProductCostsBatchQuery query = new GetProductCostsBatchQuery
        {
            ProductIds = new List<Guid> { productId },
            AsOf = null,
        };

        // Act
        GetProductCostsBatchResponse response = await _handler.Handle(query, CancellationToken.None);

        // Assert
        response.AsOfDate.Should().Be(today);
        response.Items.Should().HaveCount(1);

        _costCalculatorMock.Verify(
            calculator => calculator.CalculateAsync(productId, today, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_IncompleteBreakdown_SetsIsCompleteFalse()
    {
        // Arrange
        Guid productId = Guid.NewGuid();
        DateOnly asOf = new DateOnly(2026, 3, 15);

        CostWarning warning = new CostWarning(
            CostWarningType.MissingBom,
            "No BOM found",
            null);

        CostBreakdown breakdown = new CostBreakdown(
            ProductId: productId,
            AsOfDate: asOf,
            MaterialCost: 0m,
            LogisticsCost: 0m,
            PackagingCost: 0m,
            LaborCost: 0m,
            TotalCost: 0m,
            Lines: new List<CostLine>(),
            Warnings: new List<CostWarning> { warning });

        _costCalculatorMock
            .Setup(calculator => calculator.CalculateAsync(productId, asOf, It.IsAny<CancellationToken>()))
            .ReturnsAsync(breakdown);

        GetProductCostsBatchQuery query = new GetProductCostsBatchQuery
        {
            ProductIds = new List<Guid> { productId },
            AsOf = asOf,
        };

        // Act
        GetProductCostsBatchResponse response = await _handler.Handle(query, CancellationToken.None);

        // Assert
        response.Items.Should().HaveCount(1);
        ProductCostSummaryDto summary = response.Items[0];
        summary.IsComplete.Should().BeFalse();
        summary.TotalCost.Should().Be(0m);
    }

    [Fact]
    public async Task Handle_DoesNotIncludeLinesInSummary()
    {
        // Arrange
        Guid productId = Guid.NewGuid();
        DateOnly asOf = new DateOnly(2026, 3, 15);

        CostLine line1 = new CostLine(Guid.NewGuid(), "Ткань", ComponentCategory.Material, 2m, 50m, 100m);
        CostLine line2 = new CostLine(Guid.NewGuid(), "Доставка", ComponentCategory.Logistics, 1m, 50m, 50m);

        CostBreakdown breakdown = new CostBreakdown(
            ProductId: productId,
            AsOfDate: asOf,
            MaterialCost: 100m,
            LogisticsCost: 50m,
            PackagingCost: 0m,
            LaborCost: 0m,
            TotalCost: 150m,
            Lines: new List<CostLine> { line1, line2 },
            Warnings: new List<CostWarning>());

        _costCalculatorMock
            .Setup(calculator => calculator.CalculateAsync(productId, asOf, It.IsAny<CancellationToken>()))
            .ReturnsAsync(breakdown);

        GetProductCostsBatchQuery query = new GetProductCostsBatchQuery
        {
            ProductIds = new List<Guid> { productId },
            AsOf = asOf,
        };

        // Act
        GetProductCostsBatchResponse response = await _handler.Handle(query, CancellationToken.None);

        // Assert
        response.Items.Should().HaveCount(1);
        ProductCostSummaryDto summary = response.Items[0];
        
        summary.Should().NotBeNull();
        summary.MaterialCost.Should().Be(100m);
        summary.LogisticsCost.Should().Be(50m);
        summary.TotalCost.Should().Be(150m);
    }

    [Fact]
    public async Task Handle_CallsCalculatorForEachProductInOrder()
    {
        // Arrange
        Guid productId1 = Guid.NewGuid();
        Guid productId2 = Guid.NewGuid();
        DateOnly asOf = new DateOnly(2026, 3, 15);

        List<Guid> callSequence = new List<Guid>();

        _costCalculatorMock
            .Setup(calculator => calculator.CalculateAsync(It.IsAny<Guid>(), asOf, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, DateOnly date, CancellationToken ct) =>
            {
                callSequence.Add(id);
                return new CostBreakdown(
                    ProductId: id,
                    AsOfDate: date,
                    MaterialCost: 0m,
                    LogisticsCost: 0m,
                    PackagingCost: 0m,
                    LaborCost: 0m,
                    TotalCost: 0m,
                    Lines: new List<CostLine>(),
                    Warnings: new List<CostWarning>());
            });

        GetProductCostsBatchQuery query = new GetProductCostsBatchQuery
        {
            ProductIds = new List<Guid> { productId1, productId2 },
            AsOf = asOf,
        };

        // Act
        GetProductCostsBatchResponse response = await _handler.Handle(query, CancellationToken.None);

        // Assert
        callSequence.Should().HaveCount(2);
        callSequence[0].Should().Be(productId1);
        callSequence[1].Should().Be(productId2);
    }
}