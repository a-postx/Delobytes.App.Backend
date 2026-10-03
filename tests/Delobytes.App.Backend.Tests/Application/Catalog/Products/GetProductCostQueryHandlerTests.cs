// GetProductCostQueryHandlerTests.cs

using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Application.Queries.Products.GetProductCost;
using Delobytes.App.Backend.Catalog.Application.Services.CostCalculation;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using FluentAssertions;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Catalog.Products;

/// <summary>
/// Tests for GetProductCostQueryHandler.
/// Validates the query handler correctly invokes ICostCalculator and maps the breakdown to response DTO.
/// </summary>
public class GetProductCostQueryHandlerTests
{
    private readonly Mock<IProductRepository> _productRepositoryMock;
    private readonly Mock<ICostCalculator> _costCalculatorMock;
    private readonly GetProductCostQueryHandler _handler;

    public GetProductCostQueryHandlerTests()
    {
        _productRepositoryMock = new Mock<IProductRepository>();
        _costCalculatorMock = new Mock<ICostCalculator>();
        _handler = new GetProductCostQueryHandler(_productRepositoryMock.Object, _costCalculatorMock.Object);
    }

    [Fact]
    public async Task Handle_ExistingProduct_ReturnsCompleteBreakdown()
    {
        // Arrange
        Guid productId = Guid.NewGuid();
        DateOnly asOf = new DateOnly(2026, 3, 15);

        Product product = new Product
        {
            Id = productId,
            Sku = "TEST-001",
            Name = "Test Product",
        };

        CostBreakdown breakdown = new CostBreakdown(
            ProductId: productId,
            AsOfDate: asOf,
            MaterialCost: 100m,
            LogisticsCost: 50m,
            PackagingCost: 20m,
            LaborCost: 30m,
            TotalCost: 200m,
            Lines: new List<CostLine>
            {
                new CostLine(Guid.NewGuid(), "Ткань", ComponentCategory.Material, 2m, 50m, 100m),
                new CostLine(Guid.NewGuid(), "Доставка", ComponentCategory.Logistics, 1m, 50m, 50m),
            },
            Warnings: new List<CostWarning>());

        _productRepositoryMock
            .Setup(repository => repository.GetByIdAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        _costCalculatorMock
            .Setup(calculator => calculator.CalculateAsync(productId, asOf, It.IsAny<CancellationToken>()))
            .ReturnsAsync(breakdown);

        GetProductCostQuery query = new GetProductCostQuery
        {
            ProductId = productId,
            AsOf = asOf,
        };

        // Act
        GetProductCostResponse response = await _handler.Handle(query, CancellationToken.None);

        // Assert
        response.Found.Should().BeTrue();
        response.ProductId.Should().Be(productId);
        response.AsOfDate.Should().Be(asOf);
        response.MaterialCost.Should().Be(100m);
        response.LogisticsCost.Should().Be(50m);
        response.PackagingCost.Should().Be(20m);
        response.LaborCost.Should().Be(30m);
        response.TotalCost.Should().Be(200m);
        response.IsComplete.Should().BeTrue();
        response.Lines.Should().HaveCount(2);
        response.Warnings.Should().BeEmpty();

        _productRepositoryMock.Verify(
            repository => repository.GetByIdAsync(productId, It.IsAny<CancellationToken>()),
            Times.Once);

        _costCalculatorMock.Verify(
            calculator => calculator.CalculateAsync(productId, asOf, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_ProductNotFound_ReturnsFoundFalse()
    {
        // Arrange
        Guid productId = Guid.NewGuid();
        DateOnly asOf = new DateOnly(2026, 3, 15);

        _productRepositoryMock
            .Setup(repository => repository.GetByIdAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Product?)null);

        GetProductCostQuery query = new GetProductCostQuery
        {
            ProductId = productId,
            AsOf = asOf,
        };

        // Act
        GetProductCostResponse response = await _handler.Handle(query, CancellationToken.None);

        // Assert
        response.Found.Should().BeFalse();

        _costCalculatorMock.Verify(
            calculator => calculator.CalculateAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_NullAsOf_UsesTodayForCalculation()
    {
        // Arrange
        Guid productId = Guid.NewGuid();
        DateOnly today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date);

        Product product = new Product
        {
            Id = productId,
            Sku = "TEST-002",
            Name = "Another Product",
        };

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

        _productRepositoryMock
            .Setup(repository => repository.GetByIdAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        _costCalculatorMock
            .Setup(calculator => calculator.CalculateAsync(productId, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(breakdown);

        GetProductCostQuery query = new GetProductCostQuery
        {
            ProductId = productId,
            AsOf = null,
        };

        // Act
        GetProductCostResponse response = await _handler.Handle(query, CancellationToken.None);

        // Assert
        response.Found.Should().BeTrue();
        response.AsOfDate.Should().Be(today);

        _costCalculatorMock.Verify(
            calculator => calculator.CalculateAsync(productId, today, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_IncompleteBreakdown_ReturnsWarnings()
    {
        // Arrange
        Guid productId = Guid.NewGuid();
        Guid componentId = Guid.NewGuid();
        DateOnly asOf = new DateOnly(2026, 3, 15);

        Product product = new Product
        {
            Id = productId,
            Sku = "TEST-003",
            Name = "Product With Missing Price",
        };

        CostWarning warning = new CostWarning(
            CostWarningType.MissingComponentPrice,
            "No price found for component",
            componentId);

        CostBreakdown breakdown = new CostBreakdown(
            ProductId: productId,
            AsOfDate: asOf,
            MaterialCost: 100m,
            LogisticsCost: 0m,
            PackagingCost: 0m,
            LaborCost: 0m,
            TotalCost: 100m,
            Lines: new List<CostLine>
            {
                new CostLine(Guid.NewGuid(), "Ткань", ComponentCategory.Material, 2m, 50m, 100m),
                new CostLine(componentId, "Фурнитура", ComponentCategory.Material, 3m, 0m, 0m),
            },
            Warnings: new List<CostWarning> { warning });

        _productRepositoryMock
            .Setup(repository => repository.GetByIdAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        _costCalculatorMock
            .Setup(calculator => calculator.CalculateAsync(productId, asOf, It.IsAny<CancellationToken>()))
            .ReturnsAsync(breakdown);

        GetProductCostQuery query = new GetProductCostQuery
        {
            ProductId = productId,
            AsOf = asOf,
        };

        // Act
        GetProductCostResponse response = await _handler.Handle(query, CancellationToken.None);

        // Assert
        response.Found.Should().BeTrue();
        response.IsComplete.Should().BeFalse();
        response.Lines.Should().HaveCount(2);
        response.Warnings.Should().HaveCount(1);
        response.Warnings[0].Type.Should().Be("MissingComponentPrice");
        response.Warnings[0].ComponentId.Should().Be(componentId);
    }

    [Fact]
    public async Task Handle_MapsAllCostLineProperties()
    {
        // Arrange
        Guid productId = Guid.NewGuid();
        Guid componentId = Guid.NewGuid();
        DateOnly asOf = new DateOnly(2026, 3, 15);

        Product product = new Product
        {
            Id = productId,
            Sku = "TEST-004",
            Name = "Product For Line Mapping",
        };

        CostLine line = new CostLine(
            ComponentId: componentId,
            ComponentName: "Коробка",
            Category: ComponentCategory.Packaging,
            Quantity: 5m,
            PricePerUnit: 10.5m,
            LineTotal: 52.5m);

        CostBreakdown breakdown = new CostBreakdown(
            ProductId: productId,
            AsOfDate: asOf,
            MaterialCost: 0m,
            LogisticsCost: 0m,
            PackagingCost: 52.5m,
            LaborCost: 0m,
            TotalCost: 52.5m,
            Lines: new List<CostLine> { line },
            Warnings: new List<CostWarning>());

        _productRepositoryMock
            .Setup(repository => repository.GetByIdAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        _costCalculatorMock
            .Setup(calculator => calculator.CalculateAsync(productId, asOf, It.IsAny<CancellationToken>()))
            .ReturnsAsync(breakdown);

        GetProductCostQuery query = new GetProductCostQuery
        {
            ProductId = productId,
            AsOf = asOf,
        };

        // Act
        GetProductCostResponse response = await _handler.Handle(query, CancellationToken.None);

        // Assert
        response.Lines.Should().HaveCount(1);
        GetProductCostLineDto responseLineDto = response.Lines[0];
        responseLineDto.ComponentId.Should().Be(componentId);
        responseLineDto.ComponentName.Should().Be("Коробка");
        responseLineDto.Category.Should().Be(ComponentCategory.Packaging);
        responseLineDto.Quantity.Should().Be(5m);
        responseLineDto.PricePerUnit.Should().Be(10.5m);
        responseLineDto.LineTotal.Should().Be(52.5m);
    }
}