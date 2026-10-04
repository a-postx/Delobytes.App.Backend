using System.Text.Json;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Application.Services.CostCalculation;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using FluentAssertions;
using Moq;

namespace Delobytes.App.Backend.Tests.Application.Catalog.CostCalculation;

public class ProductCostSnapshotServiceTests
{
    [Fact]
    public async Task CaptureBeforeChangeAsync_CreatesSnapshotForEachDistinctProduct()
    {
        Guid firstProductId = Guid.NewGuid();
        Guid secondProductId = Guid.NewGuid();
        DateOnly today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
        Mock<IProductCostSnapshotRepository> snapshotRepositoryMock = new Mock<IProductCostSnapshotRepository>();
        Mock<ICostCalculator> calculatorMock = new Mock<ICostCalculator>();
        List<ProductCostSnapshot> snapshots = new List<ProductCostSnapshot>();

        snapshotRepositoryMock
            .Setup(repository => repository.GetExistingProductIdsAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                today,
                "Manual",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<Guid>());
        snapshotRepositoryMock
            .Setup(repository => repository.Add(It.IsAny<ProductCostSnapshot>()))
            .Callback<ProductCostSnapshot>(snapshot => snapshots.Add(snapshot));
        calculatorMock
            .Setup(calculator => calculator.CalculateAsync(
                It.IsAny<Guid>(),
                today,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid productId, DateOnly asOf, CancellationToken _) => BuildBreakdown(productId, asOf));

        ProductCostSnapshotService service = new ProductCostSnapshotService(
            snapshotRepositoryMock.Object,
            calculatorMock.Object);

        await service.CaptureBeforeChangeAsync(
            new[] { firstProductId, firstProductId, secondProductId },
            "Manual",
            CancellationToken.None);

        snapshots.Should().HaveCount(2);
        snapshots.Select(snapshot => snapshot.ProductId)
            .Should()
            .BeEquivalentTo(new[] { firstProductId, secondProductId });
        snapshots.Should().OnlyContain(snapshot =>
            snapshot.AsOfDate == today
            && snapshot.TriggerReason == "Manual"
            && snapshot.MaterialCost == 100m
            && snapshot.LogisticsCost == 20m
            && snapshot.PackagingCost == 30m
            && snapshot.LaborCost == 40m
            && snapshot.TotalCost == 190m
            && snapshot.IsComplete);
        calculatorMock.Verify(
            calculator => calculator.CalculateAsync(
                It.IsAny<Guid>(),
                today,
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task CaptureBeforeChangeAsync_SerializesLinesAndPreservesIncompleteState()
    {
        Guid productId = Guid.NewGuid();
        DateOnly today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
        Mock<IProductCostSnapshotRepository> snapshotRepositoryMock = new Mock<IProductCostSnapshotRepository>();
        Mock<ICostCalculator> calculatorMock = new Mock<ICostCalculator>();
        ProductCostSnapshot? createdSnapshot = null;
        CostLine line = new CostLine(
            Guid.NewGuid(),
            "Component",
            ComponentCategory.Material,
            2m,
            50m,
            100m);
        CostBreakdown breakdown = new CostBreakdown(
            productId,
            today,
            100m,
            0m,
            0m,
            0m,
            100m,
            new[] { line },
            new[]
            {
                new CostWarning(
                    CostWarningType.MissingWorkRate,
                    "Work rate is missing.",
                    null),
            });

        snapshotRepositoryMock
            .Setup(repository => repository.GetExistingProductIdsAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                today,
                "WorkRateChanged",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<Guid>());
        snapshotRepositoryMock
            .Setup(repository => repository.Add(It.IsAny<ProductCostSnapshot>()))
            .Callback<ProductCostSnapshot>(snapshot => createdSnapshot = snapshot);
        calculatorMock
            .Setup(calculator => calculator.CalculateAsync(productId, today, It.IsAny<CancellationToken>()))
            .ReturnsAsync(breakdown);

        ProductCostSnapshotService service = new ProductCostSnapshotService(
            snapshotRepositoryMock.Object,
            calculatorMock.Object);

        await service.CaptureBeforeChangeAsync(
            new[] { productId },
            "WorkRateChanged",
            CancellationToken.None);

        createdSnapshot.Should().NotBeNull();
        createdSnapshot!.IsComplete.Should().BeFalse();
        createdSnapshot.TriggerReason.Should().Be("WorkRateChanged");
        CostLine[] serializedLines = JsonSerializer.Deserialize<CostLine[]>(createdSnapshot.LinesSnapshotJson)!;
        serializedLines.Should().ContainSingle();
        serializedLines[0].Should().BeEquivalentTo(line);
    }

    [Fact]
    public async Task CaptureBeforeChangeAsync_DoesNotCreateDuplicateForExistingProductAndReason()
    {
        Guid productId = Guid.NewGuid();
        DateOnly today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
        Mock<IProductCostSnapshotRepository> snapshotRepositoryMock = new Mock<IProductCostSnapshotRepository>();
        Mock<ICostCalculator> calculatorMock = new Mock<ICostCalculator>();

        snapshotRepositoryMock
            .Setup(repository => repository.GetExistingProductIdsAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(productId)),
                today,
                "ComponentPriceChanged",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<Guid> { productId });

        ProductCostSnapshotService service = new ProductCostSnapshotService(
            snapshotRepositoryMock.Object,
            calculatorMock.Object);

        await service.CaptureBeforeChangeAsync(
            new[] { productId },
            "ComponentPriceChanged",
            CancellationToken.None);

        snapshotRepositoryMock.Verify(
            repository => repository.Add(It.IsAny<ProductCostSnapshot>()),
            Times.Never);
        calculatorMock.Verify(
            calculator => calculator.CalculateAsync(
                It.IsAny<Guid>(),
                It.IsAny<DateOnly>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CaptureBeforeChangeAsync_WithNoProducts_DoesNotQueryOrCreate()
    {
        Mock<IProductCostSnapshotRepository> snapshotRepositoryMock = new Mock<IProductCostSnapshotRepository>();
        Mock<ICostCalculator> calculatorMock = new Mock<ICostCalculator>();
        ProductCostSnapshotService service = new ProductCostSnapshotService(
            snapshotRepositoryMock.Object,
            calculatorMock.Object);

        await service.CaptureBeforeChangeAsync(
            Array.Empty<Guid>(),
            "BomChanged",
            CancellationToken.None);

        snapshotRepositoryMock.VerifyNoOtherCalls();
        calculatorMock.VerifyNoOtherCalls();
    }

    private static CostBreakdown BuildBreakdown(Guid productId, DateOnly asOf)
    {
        return new CostBreakdown(
            productId,
            asOf,
            100m,
            20m,
            30m,
            40m,
            190m,
            Array.Empty<CostLine>(),
            Array.Empty<CostWarning>());
    }
}
