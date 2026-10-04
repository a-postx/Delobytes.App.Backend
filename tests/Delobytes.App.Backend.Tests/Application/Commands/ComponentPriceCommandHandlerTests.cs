using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Catalog.Application.Commands.Components.CreateComponentPrice;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Application.Services.CostCalculation;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using FluentAssertions;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Catalog;

public class ComponentPriceCommandHandlerTests
{
    private readonly Mock<IComponentRepository> _componentRepoMock = new();
    private readonly Mock<IComponentPriceRepository> _priceRepoMock = new();
    private readonly Mock<IBomLineRepository> _bomLineRepoMock = new();
    private readonly Mock<IProductCostSnapshotService> _snapshotServiceMock = new();

    private CreateComponentPriceCommandHandler BuildHandler(
        IReadOnlyCollection<Guid>? affectedProductIds = null)
    {
        // The stub is configured here and only here: a separate Setup inside a test body would be
        // silently overridden, because Moq honours the last matching configuration.
        _bomLineRepoMock
            .Setup(r => r.GetProductIdsByComponentIdAsync(
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(affectedProductIds != null ? affectedProductIds.ToList() : Array.Empty<Guid>());

        return new CreateComponentPriceCommandHandler(
            _componentRepoMock.Object,
            _priceRepoMock.Object,
            _bomLineRepoMock.Object,
            _snapshotServiceMock.Object);
    }

    private static Component BuildComponent(Guid? id = null)
    {
        Component component = new Component
        {
            Id = id ?? Guid.NewGuid(),
            Name = "Коробка 20x15x10",
            Unit = Unit.Piece,
            IsActive = true
        };

        return component;
    }

    private static Supplier BuildSupplier(Guid? id = null)
    {
        return new Supplier
        {
            Id = id ?? Guid.NewGuid(),
            Inn = "7743013902",
            Name = "ООО Поставщик",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    // ── CreateComponentPrice ───────────────────────────────────────────────────────

    [Fact]
    public async Task CreateComponentPrice_WithSupplier_CreatesNewPriceVersion()
    {
        // Arrange
        Component component = BuildComponent();
        Supplier supplier = BuildSupplier();

        _componentRepoMock
            .Setup(r => r.GetByIdAsync(component.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(component);

        _priceRepoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        CreateComponentPriceCommandHandler handler =
            BuildHandler();

        CreateComponentPriceCommand command = new CreateComponentPriceCommand
        {
            ComponentId = component.Id,
            PricePerUnit = 450.0m,
            SupplierId = supplier.Id,
            ValidFrom = new DateOnly(2026, 3, 1),
        };

        // Act
        CreateComponentPriceResponse response =
            await handler.Handle(command, CancellationToken.None);

        // Assert
        response.Id.Should().NotBe(Guid.Empty);

        _priceRepoMock.Verify(
            r => r.Add(It.Is<ComponentPrice>(p =>
                p.ComponentId == component.Id &&
                p.PricePerUnit == 450.0m &&
                p.SupplierId == supplier.Id &&
                p.ValidFrom == new DateOnly(2026, 3, 1) &&
                p.IsActive == true)),
            Times.Once);

        _priceRepoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateComponentPrice_WithoutSupplier_CreatesNewPriceWithNullSupplier()
    {
        // Arrange
        Component component = BuildComponent();

        _componentRepoMock
            .Setup(r => r.GetByIdAsync(component.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(component);

        _priceRepoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        CreateComponentPriceCommandHandler handler =
            BuildHandler();

        CreateComponentPriceCommand command = new CreateComponentPriceCommand
        {
            ComponentId = component.Id,
            PricePerUnit = 200.0m,
            SupplierId = null,
            ValidFrom = new DateOnly(2026, 2, 1),
        };

        // Act
        CreateComponentPriceResponse response =
            await handler.Handle(command, CancellationToken.None);

        // Assert
        response.Id.Should().NotBe(Guid.Empty);

        _priceRepoMock.Verify(
            r => r.Add(It.Is<ComponentPrice>(p =>
                p.ComponentId == component.Id &&
                p.PricePerUnit == 200.0m &&
                p.SupplierId == null &&
                p.IsActive == true)),
            Times.Once);
    }

    [Fact]
    public async Task CreateComponentPrice_ComponentNotFound_ReturnsNotFound()
    {
        // Arrange
        _componentRepoMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Component?)null);

        CreateComponentPriceCommandHandler handler =
            BuildHandler();

        CreateComponentPriceCommand command = new CreateComponentPriceCommand
        {
            ComponentId = Guid.NewGuid(),
            PricePerUnit = 100.0m,
            ValidFrom = new DateOnly(2026, 1, 1),
        };

        // Act
        CreateComponentPriceResponse response =
            await handler.Handle(command, CancellationToken.None);

        // Assert
        response.Found.Should().BeFalse();
        _priceRepoMock.Verify(r => r.Add(It.IsAny<ComponentPrice>()), Times.Never);
        _priceRepoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateComponentPrice_MultipleVersions_AllowsHistoricalPrices()
    {
        // Arrange
        Component component = BuildComponent();
        Supplier oldSupplier = BuildSupplier();
        Supplier newSupplier = BuildSupplier();

        component.Prices.Add(new ComponentPrice
        {
            Id = Guid.NewGuid(),
            ComponentId = component.Id,
            PricePerUnit = 100.0m,
            SupplierId = oldSupplier.Id,
            ValidFrom = new DateOnly(2025, 1, 1),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow.AddMonths(-6),
        });

        _componentRepoMock
            .Setup(r => r.GetByIdAsync(component.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(component);

        _priceRepoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        CreateComponentPriceCommandHandler handler =
            BuildHandler();

        CreateComponentPriceCommand command = new CreateComponentPriceCommand
        {
            ComponentId = component.Id,
            PricePerUnit = 150.0m,
            SupplierId = newSupplier.Id,
            ValidFrom = new DateOnly(2026, 1, 1),
        };

        // Act
        CreateComponentPriceResponse response =
            await handler.Handle(command, CancellationToken.None);

        // Assert
        response.Id.Should().NotBe(Guid.Empty);
        response.Found.Should().BeTrue();

        _priceRepoMock.Verify(
            r => r.Add(It.Is<ComponentPrice>(p =>
                p.PricePerUnit == 150.0m &&
                p.SupplierId == newSupplier.Id &&
                p.ValidFrom == new DateOnly(2026, 1, 1))),
            Times.Once);
    }

    [Fact]
    public async Task CreateComponentPrice_PreservesExistingPrices()
    {
        // Arrange
        Component component = BuildComponent();
        ComponentPrice existingPrice = new ComponentPrice
        {
            Id = Guid.NewGuid(),
            ComponentId = component.Id,
            PricePerUnit = 100.0m,
            SupplierId = null,
            ValidFrom = new DateOnly(2025, 1, 1),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow.AddMonths(-3),
        };

        component.Prices.Add(existingPrice);

        _componentRepoMock
            .Setup(r => r.GetByIdAsync(component.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(component);

        _priceRepoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        CreateComponentPriceCommandHandler handler =
            BuildHandler();

        CreateComponentPriceCommand command = new CreateComponentPriceCommand
        {
            ComponentId = component.Id,
            PricePerUnit = 120.0m,
            SupplierId = null,
            ValidFrom = new DateOnly(2026, 4, 1),
        };

        // Act
        await handler.Handle(command, CancellationToken.None);

        // Assert
        component.Prices.Should().Contain(existingPrice);
        existingPrice.PricePerUnit.Should().Be(100.0m);
        existingPrice.ValidFrom.Should().Be(new DateOnly(2025, 1, 1));
    }

    [Fact]
    public async Task CreateComponentPrice_SupplierChange_CreatesNewVersion()
    {
        // Arrange
        Component component = BuildComponent();
        Supplier supplier1 = BuildSupplier();
        Supplier supplier2 = BuildSupplier();

        _componentRepoMock
            .Setup(r => r.GetByIdAsync(component.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(component);

        _priceRepoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        CreateComponentPriceCommandHandler handler =
            BuildHandler();

        CreateComponentPriceCommand command = new CreateComponentPriceCommand
        {
            ComponentId = component.Id,
            PricePerUnit = 250.0m,
            SupplierId = supplier2.Id,
            ValidFrom = new DateOnly(2026, 5, 1),
        };

        // Act
        CreateComponentPriceResponse response =
            await handler.Handle(command, CancellationToken.None);

        // Assert
        response.Found.Should().BeTrue();

        _priceRepoMock.Verify(
            r => r.Add(It.Is<ComponentPrice>(p =>
                p.SupplierId == supplier2.Id &&
                p.PricePerUnit == 250.0m)),
            Times.Once);
    }

    [Fact]
    public async Task CreateComponentPrice_SamePriceNewDate_CreatesNewVersion()
    {
        // Arrange
        Component component = BuildComponent();

        _componentRepoMock
            .Setup(r => r.GetByIdAsync(component.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(component);

        _priceRepoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        CreateComponentPriceCommandHandler handler =
            BuildHandler();

        CreateComponentPriceCommand command = new CreateComponentPriceCommand
        {
            ComponentId = component.Id,
            PricePerUnit = 100.0m,
            SupplierId = null,
            ValidFrom = new DateOnly(2026, 6, 1),
        };

        // Act
        CreateComponentPriceResponse response =
            await handler.Handle(command, CancellationToken.None);

        // Assert
        response.Found.Should().BeTrue();

        _priceRepoMock.Verify(
            r => r.Add(It.Is<ComponentPrice>(p =>
                p.PricePerUnit == 100.0m &&
                p.ValidFrom == new DateOnly(2026, 6, 1))),
            Times.Once);
    }

    // ── Этап 5: автоматическая фиксация снапшота ─────────────────────────────────────

    [Fact]
    public async Task CreateComponentPrice_UsedByProducts_CapturesSnapshotForEveryAffectedProduct()
    {
        // Arrange
        Component component = BuildComponent();
        Guid firstProductId = Guid.NewGuid();
        Guid secondProductId = Guid.NewGuid();

        _componentRepoMock
            .Setup(r => r.GetByIdAsync(component.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(component);

        _priceRepoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        CreateComponentPriceCommandHandler handler = BuildHandler(new[] { firstProductId, secondProductId });

        CreateComponentPriceCommand command = new CreateComponentPriceCommand
        {
            ComponentId = component.Id,
            PricePerUnit = 200.0m,
            ValidFrom = new DateOnly(2026, 7, 1),
        };

        // Act
        await handler.Handle(command, CancellationToken.None);

        // Assert
        _bomLineRepoMock.Verify(
            r => r.GetProductIdsByComponentIdAsync(component.Id, It.IsAny<CancellationToken>()),
            Times.Once);

        _snapshotServiceMock.Verify(
            s => s.CaptureBeforeChangeAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids =>
                    ids.Count == 2 &&
                    ids.Contains(firstProductId) &&
                    ids.Contains(secondProductId)),
                "ComponentPriceChanged",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateComponentPrice_CapturesSnapshotBeforePreviousPriceIsDeactivated()
    {
        // Arrange
        Component component = BuildComponent();
        ComponentPrice previous = new ComponentPrice
        {
            Id = Guid.NewGuid(),
            ComponentId = component.Id,
            PricePerUnit = 100.0m,
            SupplierId = null,
            ValidFrom = new DateOnly(2025, 1, 1),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow.AddMonths(-1),
        };

        Guid productId = Guid.NewGuid();
        bool previousPriceWasActiveAtCapture = false;

        _componentRepoMock
            .Setup(r => r.GetByIdAsync(component.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(component);

        _priceRepoMock
            .Setup(r => r.GetActiveByComponentIdAsync(component.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(previous);

        _priceRepoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _snapshotServiceMock
            .Setup(s => s.CaptureBeforeChangeAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyCollection<Guid>, string, CancellationToken>(
                (_, _, _) =>
                {
                    // Touching IsActive in memory before the capture would make the calculation
                    // inside the snapshot ignore the price that is still in force, so the
                    // capture has to happen while the old version is untouched.
                    previousPriceWasActiveAtCapture = previous.IsActive;
                })
            .Returns(Task.CompletedTask);

        CreateComponentPriceCommandHandler handler = BuildHandler(new[] { productId });

        CreateComponentPriceCommand command = new CreateComponentPriceCommand
        {
            ComponentId = component.Id,
            PricePerUnit = 250.0m,
            ValidFrom = new DateOnly(2026, 8, 1),
        };

        // Act
        await handler.Handle(command, CancellationToken.None);

        // Assert
        previousPriceWasActiveAtCapture.Should().BeTrue();
        previous.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task CreateComponentPrice_UnknownComponent_DoesNotCaptureSnapshot()
    {
        // Arrange
        _componentRepoMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Component?)null);

        CreateComponentPriceCommandHandler handler = BuildHandler();

        CreateComponentPriceCommand command = new CreateComponentPriceCommand
        {
            ComponentId = Guid.NewGuid(),
            PricePerUnit = 100.0m,
            ValidFrom = new DateOnly(2026, 1, 1),
        };

        // Act
        await handler.Handle(command, CancellationToken.None);

        // Assert
        _snapshotServiceMock.Verify(
            s => s.CaptureBeforeChangeAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateComponentPrice_NotUsedByAnyProduct_StillCallsCaptureWithEmptySet()
    {
        // Arrange
        Component component = BuildComponent();

        _componentRepoMock
            .Setup(r => r.GetByIdAsync(component.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(component);

        _priceRepoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        CreateComponentPriceCommandHandler handler = BuildHandler(Array.Empty<Guid>());

        CreateComponentPriceCommand command = new CreateComponentPriceCommand
        {
            ComponentId = component.Id,
            PricePerUnit = 300.0m,
            ValidFrom = new DateOnly(2026, 9, 1),
        };

        // Act
        await handler.Handle(command, CancellationToken.None);

        // Assert
        _snapshotServiceMock.Verify(
            s => s.CaptureBeforeChangeAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 0),
                "ComponentPriceChanged",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
