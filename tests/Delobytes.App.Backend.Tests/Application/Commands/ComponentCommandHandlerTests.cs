using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Catalog.Application.Commands.Components.CreateComponent;
using Delobytes.App.Backend.Catalog.Application.Commands.Components.CreateComponentPrice;
using Delobytes.App.Backend.Catalog.Application.Commands.Components.DeleteComponent;
using Delobytes.App.Backend.Catalog.Application.Commands.Components.RestoreComponent;
using Delobytes.App.Backend.Catalog.Application.Commands.Components.UpdateComponent;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Application.Queries.Components;
using Delobytes.App.Backend.Catalog.Application.Queries.Components.GetComponent;
using Delobytes.App.Backend.Catalog.Application.Queries.Components.GetComponents;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using FluentAssertions;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Catalog;

public class ComponentCommandHandlerTests
{
    private readonly Mock<IComponentRepository> _repoMock = new();
    private readonly Mock<IComponentPriceRepository> _priceRepoMock = new();

    private static readonly Guid _supplierId = Guid.NewGuid();

    private static ComponentPrice BuildPrice(
        Guid componentId,
        bool isActive = true,
        decimal pricePerUnit = 12.50m,
        DateOnly? validFrom = null)
        => new ComponentPrice
        {
            Id = Guid.NewGuid(),
            ComponentId = componentId,
            PricePerUnit = pricePerUnit,
            SupplierId = _supplierId,
            ValidFrom = validFrom ?? DateOnly.FromDateTime(DateTime.UtcNow),
            IsActive = isActive,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private static Component BuildComponent(Guid? id = null, bool withActivePrice = true, bool isActive = true)
    {
        Component component = new Component
        {
            Id = id ?? Guid.NewGuid(),
            Name = "Коробка 20x15x10",
            Unit = Unit.Piece,
            IsActive = isActive,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        if (withActivePrice)
        {
            component.Prices.Add(BuildPrice(component.Id, isActive: isActive));
        }

        return component;
    }

    // ── Create ──────────────────────────────────────────────────────

    [Fact]
    public async Task CreateComponent_ValidCommand_AddsComponentAndPriceAtomically()
    {
        // Arrange
        _repoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        CreateComponentCommandHandler handler =
            new CreateComponentCommandHandler(_repoMock.Object, _priceRepoMock.Object);

        Guid supplierId = Guid.NewGuid();
        DateOnly validFrom = new DateOnly(2026, 1, 1);

        CreateComponentCommand command = new CreateComponentCommand
        {
            Name = "Пузырчатая плёнка",
            Description = "Рулон 1м x 50м",
            Unit = Unit.Meter,
            PricePerUnit = 350m,
            SupplierId = supplierId,
            ValidFrom = validFrom,
        };

        // Act
        CreateComponentResponse response =
            await handler.Handle(command, CancellationToken.None);

        // Assert
        response.Id.Should().NotBe(Guid.Empty);

        _repoMock.Verify(
            r => r.Add(It.Is<Component>(c =>
                c.Id == response.Id &&
                c.Name == "Пузырчатая плёнка" &&
                c.Unit == Unit.Meter &&
                c.IsActive == true)),
            Times.Once);

        _priceRepoMock.Verify(
            p => p.Add(It.Is<ComponentPrice>(price =>
                price.ComponentId == response.Id &&
                price.PricePerUnit == 350m &&
                price.SupplierId == supplierId &&
                price.ValidFrom == validFrom &&
                price.IsActive == true)),
            Times.Once);

        // Component and price are persisted through the same scoped context in one SaveChanges call.
        _repoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _priceRepoMock.Verify(p => p.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateComponent_NullableFieldsArePreserved()
    {
        // Arrange
        Component? capturedComponent = null;
        ComponentPrice? capturedPrice = null;

        _repoMock
            .Setup(r => r.Add(It.IsAny<Component>()))
            .Callback<Component>(c => capturedComponent = c);

        _priceRepoMock
            .Setup(p => p.Add(It.IsAny<ComponentPrice>()))
            .Callback<ComponentPrice>(p => capturedPrice = p);

        _repoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        CreateComponentCommandHandler handler =
            new CreateComponentCommandHandler(_repoMock.Object, _priceRepoMock.Object);

        CreateComponentCommand command = new CreateComponentCommand
        {
            Name = "Скотч",
            Unit = Unit.Piece,
            PricePerUnit = 25m,
            Description = null,
            SupplierId = null,
            ValidFrom = DateOnly.FromDateTime(DateTime.UtcNow),
        };

        // Act
        await handler.Handle(command, CancellationToken.None);

        // Assert
        capturedComponent.Should().NotBeNull();
        capturedComponent!.Description.Should().BeNull();

        capturedPrice.Should().NotBeNull();
        capturedPrice!.SupplierId.Should().BeNull();
    }

    // ── Update ──────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateComponent_ExistingId_UpdatesDescriptiveFieldsOnly()
    {
        // Arrange
        Component existing = BuildComponent();
        ComponentPrice originalPrice = existing.Prices.First();
        decimal originalPricePerUnit = originalPrice.PricePerUnit;
        Guid? originalSupplierId = originalPrice.SupplierId;

        _repoMock
            .Setup(r => r.GetByIdAsync(existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        _repoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        UpdateComponentCommandHandler handler =
            new UpdateComponentCommandHandler(_repoMock.Object);

        UpdateComponentCommand command = new UpdateComponentCommand
        {
            Id = existing.Id,
            Name = "Коробка 30x20x15",
            Description = "Обновлённое описание",
            Unit = Unit.Kg,
        };

        // Act
        UpdateComponentResponse response =
            await handler.Handle(command, CancellationToken.None);

        // Assert
        response.Found.Should().BeTrue();
        existing.Name.Should().Be("Коробка 30x20x15");
        existing.Description.Should().Be("Обновлённое описание");
        existing.Unit.Should().Be(Unit.Kg);

        // Price versioning invariant — updating descriptive fields must never touch the price.
        originalPrice.PricePerUnit.Should().Be(originalPricePerUnit);
        originalPrice.SupplierId.Should().Be(originalSupplierId);

        _repoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _priceRepoMock.Verify(p => p.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateComponent_NotFound_ReturnsFalse()
    {
        // Arrange
        _repoMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Component?)null);

        UpdateComponentCommandHandler handler =
            new UpdateComponentCommandHandler(_repoMock.Object);

        UpdateComponentCommand command = new UpdateComponentCommand
        {
            Id = Guid.NewGuid(),
            Name = "X",
            Unit = Unit.Piece,
        };

        // Act
        UpdateComponentResponse response =
            await handler.Handle(command, CancellationToken.None);

        // Assert
        response.Found.Should().BeFalse();
        _repoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Delete ──────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteComponent_ExistingId_DeactivatesComponentAndActivePrice()
    {
        // Arrange
        Component existing = BuildComponent();
        ComponentPrice activePrice = existing.Prices.First();
        DateOnly originalValidFrom = activePrice.ValidFrom;

        _repoMock
            .Setup(r => r.GetByIdAsync(existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        _priceRepoMock
            .Setup(p => p.GetActiveByComponentIdAsync(existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(activePrice);

        _repoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        DeleteComponentCommandHandler handler =
            new DeleteComponentCommandHandler(_repoMock.Object, _priceRepoMock.Object);

        // Act
        DeleteComponentResponse response =
            await handler.Handle(
                new DeleteComponentCommand { Id = existing.Id },
                CancellationToken.None);

        // Assert
        response.Found.Should().BeTrue();
        existing.IsActive.Should().BeFalse();
        activePrice.IsActive.Should().BeFalse();
        activePrice.UpdatedAt.Should().NotBeNull();

        // Deactivation must never rewrite ValidFrom — history ordering stays intact.
        activePrice.ValidFrom.Should().Be(originalValidFrom);

        _repoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteComponent_NoActivePrice_StillDeactivatesComponent()
    {
        // Arrange
        Component existing = BuildComponent(withActivePrice: false);

        _repoMock
            .Setup(r => r.GetByIdAsync(existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        _priceRepoMock
            .Setup(p => p.GetActiveByComponentIdAsync(existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ComponentPrice?)null);

        _repoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        DeleteComponentCommandHandler handler =
            new DeleteComponentCommandHandler(_repoMock.Object, _priceRepoMock.Object);

        // Act
        DeleteComponentResponse response =
            await handler.Handle(
                new DeleteComponentCommand { Id = existing.Id },
                CancellationToken.None);

        // Assert
        response.Found.Should().BeTrue();
        existing.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteComponent_NotFound_ReturnsFalse()
    {
        // Arrange
        _repoMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Component?)null);

        DeleteComponentCommandHandler handler =
            new DeleteComponentCommandHandler(_repoMock.Object, _priceRepoMock.Object);

        // Act
        DeleteComponentResponse response =
            await handler.Handle(
                new DeleteComponentCommand { Id = Guid.NewGuid() },
                CancellationToken.None);

        // Assert
        response.Found.Should().BeFalse();
        _repoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _priceRepoMock.Verify(p => p.GetActiveByComponentIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Restore ─────────────────────────────────────────────────────

    [Fact]
    public async Task RestoreComponent_ExistingId_ReactivatesComponentAndLatestPrice()
    {
        // Arrange
        Component existing = BuildComponent(isActive: false);
        ComponentPrice latestPrice = existing.Prices.First();
        latestPrice.IsActive = false;
        DateOnly originalValidFrom = latestPrice.ValidFrom;

        _repoMock
            .Setup(r => r.GetByIdAsync(existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        _priceRepoMock
            .Setup(p => p.GetLatestByComponentIdAsync(existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(latestPrice);

        _repoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        RestoreComponentCommandHandler handler =
            new RestoreComponentCommandHandler(_repoMock.Object, _priceRepoMock.Object);

        // Act
        RestoreComponentResponse response =
            await handler.Handle(
                new RestoreComponentCommand { Id = existing.Id },
                CancellationToken.None);

        // Assert
        response.Found.Should().BeTrue();
        existing.IsActive.Should().BeTrue();
        latestPrice.IsActive.Should().BeTrue();
        latestPrice.UpdatedAt.Should().NotBeNull();

        // Restoring must never rewrite ValidFrom.
        latestPrice.ValidFrom.Should().Be(originalValidFrom);

        _repoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RestoreComponent_NotFound_ReturnsFalse()
    {
        // Arrange
        _repoMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Component?)null);

        RestoreComponentCommandHandler handler =
            new RestoreComponentCommandHandler(_repoMock.Object, _priceRepoMock.Object);

        // Act
        RestoreComponentResponse response =
            await handler.Handle(
                new RestoreComponentCommand { Id = Guid.NewGuid() },
                CancellationToken.None);

        // Assert
        response.Found.Should().BeFalse();
        _repoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Create price version ────────────────────────────────────────

    [Fact]
    public async Task CreateComponentPrice_ValidCommand_DeactivatesPreviousAndAddsNewVersion()
    {
        // Arrange
        Component existing = BuildComponent();
        ComponentPrice oldPrice = existing.Prices.First();
        DateOnly oldValidFrom = oldPrice.ValidFrom;

        _repoMock
            .Setup(r => r.GetByIdAsync(existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        _priceRepoMock
            .Setup(p => p.GetActiveByComponentIdAsync(existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(oldPrice);

        _priceRepoMock
            .Setup(p => p.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        CreateComponentPriceCommandHandler handler =
            new CreateComponentPriceCommandHandler(_repoMock.Object, _priceRepoMock.Object);

        Guid newSupplierId = Guid.NewGuid();
        DateOnly newValidFrom = new DateOnly(2026, 6, 1);

        CreateComponentPriceCommand command = new CreateComponentPriceCommand
        {
            ComponentId = existing.Id,
            PricePerUnit = 99m,
            SupplierId = newSupplierId,
            ValidFrom = newValidFrom,
        };

        // Act
        CreateComponentPriceResponse response =
            await handler.Handle(command, CancellationToken.None);

        // Assert
        response.Found.Should().BeTrue();
        response.Id.Should().NotBe(Guid.Empty);

        // The previous version is deactivated, never overwritten in place.
        oldPrice.IsActive.Should().BeFalse();
        oldPrice.UpdatedAt.Should().NotBeNull();
        oldPrice.ValidFrom.Should().Be(oldValidFrom);
        oldPrice.PricePerUnit.Should().Be(12.50m);

        _priceRepoMock.Verify(
            p => p.Add(It.Is<ComponentPrice>(price =>
                price.ComponentId == existing.Id &&
                price.PricePerUnit == 99m &&
                price.SupplierId == newSupplierId &&
                price.ValidFrom == newValidFrom &&
                price.IsActive == true)),
            Times.Once);

        _priceRepoMock.Verify(p => p.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateComponentPrice_ComponentNotFound_ReturnsFoundFalse()
    {
        // Arrange
        _repoMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Component?)null);

        CreateComponentPriceCommandHandler handler =
            new CreateComponentPriceCommandHandler(_repoMock.Object, _priceRepoMock.Object);

        CreateComponentPriceCommand command = new CreateComponentPriceCommand
        {
            ComponentId = Guid.NewGuid(),
            PricePerUnit = 10m,
            ValidFrom = DateOnly.FromDateTime(DateTime.UtcNow),
        };

        // Act
        CreateComponentPriceResponse response =
            await handler.Handle(command, CancellationToken.None);

        // Assert
        response.Found.Should().BeFalse();
        _priceRepoMock.Verify(p => p.Add(It.IsAny<ComponentPrice>()), Times.Never);
        _priceRepoMock.Verify(p => p.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateComponentPrice_NoCurrentActivePrice_StillCreatesNewVersion()
    {
        // Arrange
        Component existing = BuildComponent(withActivePrice: false);

        _repoMock
            .Setup(r => r.GetByIdAsync(existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        _priceRepoMock
            .Setup(p => p.GetActiveByComponentIdAsync(existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ComponentPrice?)null);

        _priceRepoMock
            .Setup(p => p.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        CreateComponentPriceCommandHandler handler =
            new CreateComponentPriceCommandHandler(_repoMock.Object, _priceRepoMock.Object);

        CreateComponentPriceCommand command = new CreateComponentPriceCommand
        {
            ComponentId = existing.Id,
            PricePerUnit = 42m,
            ValidFrom = DateOnly.FromDateTime(DateTime.UtcNow),
        };

        // Act
        CreateComponentPriceResponse response =
            await handler.Handle(command, CancellationToken.None);

        // Assert
        response.Found.Should().BeTrue();
        _priceRepoMock.Verify(p => p.Add(It.IsAny<ComponentPrice>()), Times.Once);
    }

    // ── Query handlers ──────────────────────────────────────────────

    [Fact]
    public async Task GetComponents_ReturnsItemsWithActivePriceMapped()
    {
        // Arrange
        List<Component> components = new List<Component>
        {
            BuildComponent(),
            BuildComponent(),
        };

        _repoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(components);

        GetComponentsQueryHandler handler =
            new GetComponentsQueryHandler(_repoMock.Object);

        // Act
        GetComponentsResponse response =
            await handler.Handle(new GetComponentsQuery(), CancellationToken.None);

        // Assert
        response.Items.Should().HaveCount(2);
        response.Items.Should().OnlyContain(i => i.ActivePrice != null && i.ActivePrice.PricePerUnit == 12.50m);
    }

    [Fact]
    public async Task GetComponent_ExistingId_ReturnsMappedResponseWithActivePrice()
    {
        // Arrange
        Component component = BuildComponent();
        ComponentPrice activePrice = component.Prices.First();

        _repoMock
            .Setup(r => r.GetWithPricesByIdAsync(component.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(component);

        GetComponentQueryHandler handler =
            new GetComponentQueryHandler(_repoMock.Object);

        // Act
        GetComponentResponse? response =
            await handler.Handle(
                new GetComponentQuery { Id = component.Id },
                CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response!.Name.Should().Be(component.Name);
        response.ActivePrice.Should().NotBeNull();
        response.ActivePrice!.SupplierId.Should().Be(activePrice.SupplierId);
        response.ActivePrice.PricePerUnit.Should().Be(activePrice.PricePerUnit);
    }

    [Fact]
    public async Task GetComponent_NoActivePrice_ReturnsNullActivePrice()
    {
        // Arrange
        Component component = BuildComponent(withActivePrice: false);

        _repoMock
            .Setup(r => r.GetWithPricesByIdAsync(component.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(component);

        GetComponentQueryHandler handler =
            new GetComponentQueryHandler(_repoMock.Object);

        // Act
        GetComponentResponse? response =
            await handler.Handle(
                new GetComponentQuery { Id = component.Id },
                CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response!.ActivePrice.Should().BeNull();
    }

    [Fact]
    public async Task GetComponent_NotFound_ReturnsNull()
    {
        // Arrange
        _repoMock
            .Setup(r => r.GetWithPricesByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Component?)null);

        GetComponentQueryHandler handler =
            new GetComponentQueryHandler(_repoMock.Object);

        // Act
        GetComponentResponse? response =
            await handler.Handle(
                new GetComponentQuery { Id = Guid.NewGuid() },
                CancellationToken.None);

        // Assert
        response.Should().BeNull();
    }
}
