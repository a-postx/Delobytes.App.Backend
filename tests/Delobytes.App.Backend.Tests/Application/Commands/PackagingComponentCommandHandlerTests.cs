using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Catalog.Application.Commands.PackagingComponents.CreatePackagingComponent;
using Delobytes.App.Backend.Catalog.Application.Commands.PackagingComponents.CreatePackagingComponentPrice;
using Delobytes.App.Backend.Catalog.Application.Commands.PackagingComponents.DeletePackagingComponent;
using Delobytes.App.Backend.Catalog.Application.Commands.PackagingComponents.RestorePackagingComponent;
using Delobytes.App.Backend.Catalog.Application.Commands.PackagingComponents.UpdatePackagingComponent;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Application.Queries.PackagingComponents;
using Delobytes.App.Backend.Catalog.Application.Queries.PackagingComponents.GetPackagingComponent;
using Delobytes.App.Backend.Catalog.Application.Queries.PackagingComponents.GetPackagingComponents;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using FluentAssertions;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Catalog;

public class PackagingComponentCommandHandlerTests
{
    private readonly Mock<IPackagingComponentRepository> _repoMock = new();
    private readonly Mock<IPackagingComponentPriceRepository> _priceRepoMock = new();

    private static readonly Guid _supplierId = Guid.NewGuid();

    private static PackagingComponentPrice BuildPrice(
        Guid componentId,
        bool isActive = true,
        decimal pricePerUnit = 12.50m,
        DateOnly? validFrom = null)
        => new PackagingComponentPrice
        {
            Id = Guid.NewGuid(),
            PackagingComponentId = componentId,
            PricePerUnit = pricePerUnit,
            SupplierId = _supplierId,
            ValidFrom = validFrom ?? DateOnly.FromDateTime(DateTime.UtcNow),
            IsActive = isActive,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private static PackagingComponent BuildComponent(Guid? id = null, bool withActivePrice = true, bool isActive = true)
    {
        PackagingComponent component = new PackagingComponent
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
    public async Task CreatePackagingComponent_ValidCommand_AddsComponentAndPriceAtomically()
    {
        // Arrange
        _repoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        CreatePackagingComponentCommandHandler handler =
            new CreatePackagingComponentCommandHandler(_repoMock.Object, _priceRepoMock.Object);

        Guid supplierId = Guid.NewGuid();
        DateOnly validFrom = new DateOnly(2026, 1, 1);

        CreatePackagingComponentCommand command = new CreatePackagingComponentCommand
        {
            Name = "Пузырчатая плёнка",
            Description = "Рулон 1м x 50м",
            Unit = Unit.Meter,
            PricePerUnit = 350m,
            SupplierId = supplierId,
            ValidFrom = validFrom,
        };

        // Act
        CreatePackagingComponentResponse response =
            await handler.Handle(command, CancellationToken.None);

        // Assert
        response.Id.Should().NotBe(Guid.Empty);

        _repoMock.Verify(
            r => r.Add(It.Is<PackagingComponent>(c =>
                c.Id == response.Id &&
                c.Name == "Пузырчатая плёнка" &&
                c.Unit == Unit.Meter &&
                c.IsActive == true)),
            Times.Once);

        _priceRepoMock.Verify(
            p => p.Add(It.Is<PackagingComponentPrice>(price =>
                price.PackagingComponentId == response.Id &&
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
    public async Task CreatePackagingComponent_NullableFieldsArePreserved()
    {
        // Arrange
        PackagingComponent? capturedComponent = null;
        PackagingComponentPrice? capturedPrice = null;

        _repoMock
            .Setup(r => r.Add(It.IsAny<PackagingComponent>()))
            .Callback<PackagingComponent>(c => capturedComponent = c);

        _priceRepoMock
            .Setup(p => p.Add(It.IsAny<PackagingComponentPrice>()))
            .Callback<PackagingComponentPrice>(p => capturedPrice = p);

        _repoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        CreatePackagingComponentCommandHandler handler =
            new CreatePackagingComponentCommandHandler(_repoMock.Object, _priceRepoMock.Object);

        CreatePackagingComponentCommand command = new CreatePackagingComponentCommand
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
    public async Task UpdatePackagingComponent_ExistingId_UpdatesDescriptiveFieldsOnly()
    {
        // Arrange
        PackagingComponent existing = BuildComponent();
        PackagingComponentPrice originalPrice = existing.Prices.First();
        decimal originalPricePerUnit = originalPrice.PricePerUnit;
        Guid? originalSupplierId = originalPrice.SupplierId;

        _repoMock
            .Setup(r => r.GetByIdAsync(existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        _repoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        UpdatePackagingComponentCommandHandler handler =
            new UpdatePackagingComponentCommandHandler(_repoMock.Object);

        UpdatePackagingComponentCommand command = new UpdatePackagingComponentCommand
        {
            Id = existing.Id,
            Name = "Коробка 30x20x15",
            Description = "Обновлённое описание",
            Unit = Unit.Kg,
        };

        // Act
        UpdatePackagingComponentResponse response =
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
    public async Task UpdatePackagingComponent_NotFound_ReturnsFalse()
    {
        // Arrange
        _repoMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PackagingComponent?)null);

        UpdatePackagingComponentCommandHandler handler =
            new UpdatePackagingComponentCommandHandler(_repoMock.Object);

        UpdatePackagingComponentCommand command = new UpdatePackagingComponentCommand
        {
            Id = Guid.NewGuid(),
            Name = "X",
            Unit = Unit.Piece,
        };

        // Act
        UpdatePackagingComponentResponse response =
            await handler.Handle(command, CancellationToken.None);

        // Assert
        response.Found.Should().BeFalse();
        _repoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Delete ──────────────────────────────────────────────────────

    [Fact]
    public async Task DeletePackagingComponent_ExistingId_DeactivatesComponentAndActivePrice()
    {
        // Arrange
        PackagingComponent existing = BuildComponent();
        PackagingComponentPrice activePrice = existing.Prices.First();
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

        DeletePackagingComponentCommandHandler handler =
            new DeletePackagingComponentCommandHandler(_repoMock.Object, _priceRepoMock.Object);

        // Act
        DeletePackagingComponentResponse response =
            await handler.Handle(
                new DeletePackagingComponentCommand { Id = existing.Id },
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
    public async Task DeletePackagingComponent_NoActivePrice_StillDeactivatesComponent()
    {
        // Arrange
        PackagingComponent existing = BuildComponent(withActivePrice: false);

        _repoMock
            .Setup(r => r.GetByIdAsync(existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        _priceRepoMock
            .Setup(p => p.GetActiveByComponentIdAsync(existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PackagingComponentPrice?)null);

        _repoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        DeletePackagingComponentCommandHandler handler =
            new DeletePackagingComponentCommandHandler(_repoMock.Object, _priceRepoMock.Object);

        // Act
        DeletePackagingComponentResponse response =
            await handler.Handle(
                new DeletePackagingComponentCommand { Id = existing.Id },
                CancellationToken.None);

        // Assert
        response.Found.Should().BeTrue();
        existing.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task DeletePackagingComponent_NotFound_ReturnsFalse()
    {
        // Arrange
        _repoMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PackagingComponent?)null);

        DeletePackagingComponentCommandHandler handler =
            new DeletePackagingComponentCommandHandler(_repoMock.Object, _priceRepoMock.Object);

        // Act
        DeletePackagingComponentResponse response =
            await handler.Handle(
                new DeletePackagingComponentCommand { Id = Guid.NewGuid() },
                CancellationToken.None);

        // Assert
        response.Found.Should().BeFalse();
        _repoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _priceRepoMock.Verify(p => p.GetActiveByComponentIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Restore ─────────────────────────────────────────────────────

    [Fact]
    public async Task RestorePackagingComponent_ExistingId_ReactivatesComponentAndLatestPrice()
    {
        // Arrange
        PackagingComponent existing = BuildComponent(isActive: false);
        PackagingComponentPrice latestPrice = existing.Prices.First();
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

        RestorePackagingComponentCommandHandler handler =
            new RestorePackagingComponentCommandHandler(_repoMock.Object, _priceRepoMock.Object);

        // Act
        RestorePackagingComponentResponse response =
            await handler.Handle(
                new RestorePackagingComponentCommand { Id = existing.Id },
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
    public async Task RestorePackagingComponent_NotFound_ReturnsFalse()
    {
        // Arrange
        _repoMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PackagingComponent?)null);

        RestorePackagingComponentCommandHandler handler =
            new RestorePackagingComponentCommandHandler(_repoMock.Object, _priceRepoMock.Object);

        // Act
        RestorePackagingComponentResponse response =
            await handler.Handle(
                new RestorePackagingComponentCommand { Id = Guid.NewGuid() },
                CancellationToken.None);

        // Assert
        response.Found.Should().BeFalse();
        _repoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Create price version ────────────────────────────────────────

    [Fact]
    public async Task CreatePackagingComponentPrice_ValidCommand_DeactivatesPreviousAndAddsNewVersion()
    {
        // Arrange
        PackagingComponent existing = BuildComponent();
        PackagingComponentPrice oldPrice = existing.Prices.First();
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

        CreatePackagingComponentPriceCommandHandler handler =
            new CreatePackagingComponentPriceCommandHandler(_repoMock.Object, _priceRepoMock.Object);

        Guid newSupplierId = Guid.NewGuid();
        DateOnly newValidFrom = new DateOnly(2026, 6, 1);

        CreatePackagingComponentPriceCommand command = new CreatePackagingComponentPriceCommand
        {
            PackagingComponentId = existing.Id,
            PricePerUnit = 99m,
            SupplierId = newSupplierId,
            ValidFrom = newValidFrom,
        };

        // Act
        CreatePackagingComponentPriceResponse response =
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
            p => p.Add(It.Is<PackagingComponentPrice>(price =>
                price.PackagingComponentId == existing.Id &&
                price.PricePerUnit == 99m &&
                price.SupplierId == newSupplierId &&
                price.ValidFrom == newValidFrom &&
                price.IsActive == true)),
            Times.Once);

        _priceRepoMock.Verify(p => p.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreatePackagingComponentPrice_ComponentNotFound_ReturnsFoundFalse()
    {
        // Arrange
        _repoMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PackagingComponent?)null);

        CreatePackagingComponentPriceCommandHandler handler =
            new CreatePackagingComponentPriceCommandHandler(_repoMock.Object, _priceRepoMock.Object);

        CreatePackagingComponentPriceCommand command = new CreatePackagingComponentPriceCommand
        {
            PackagingComponentId = Guid.NewGuid(),
            PricePerUnit = 10m,
            ValidFrom = DateOnly.FromDateTime(DateTime.UtcNow),
        };

        // Act
        CreatePackagingComponentPriceResponse response =
            await handler.Handle(command, CancellationToken.None);

        // Assert
        response.Found.Should().BeFalse();
        _priceRepoMock.Verify(p => p.Add(It.IsAny<PackagingComponentPrice>()), Times.Never);
        _priceRepoMock.Verify(p => p.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreatePackagingComponentPrice_NoCurrentActivePrice_StillCreatesNewVersion()
    {
        // Arrange
        PackagingComponent existing = BuildComponent(withActivePrice: false);

        _repoMock
            .Setup(r => r.GetByIdAsync(existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        _priceRepoMock
            .Setup(p => p.GetActiveByComponentIdAsync(existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PackagingComponentPrice?)null);

        _priceRepoMock
            .Setup(p => p.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        CreatePackagingComponentPriceCommandHandler handler =
            new CreatePackagingComponentPriceCommandHandler(_repoMock.Object, _priceRepoMock.Object);

        CreatePackagingComponentPriceCommand command = new CreatePackagingComponentPriceCommand
        {
            PackagingComponentId = existing.Id,
            PricePerUnit = 42m,
            ValidFrom = DateOnly.FromDateTime(DateTime.UtcNow),
        };

        // Act
        CreatePackagingComponentPriceResponse response =
            await handler.Handle(command, CancellationToken.None);

        // Assert
        response.Found.Should().BeTrue();
        _priceRepoMock.Verify(p => p.Add(It.IsAny<PackagingComponentPrice>()), Times.Once);
    }

    // ── Query handlers ──────────────────────────────────────────────

    [Fact]
    public async Task GetPackagingComponents_ReturnsItemsWithActivePriceMapped()
    {
        // Arrange
        List<PackagingComponent> components = new List<PackagingComponent>
        {
            BuildComponent(),
            BuildComponent(),
        };

        _repoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(components);

        GetPackagingComponentsQueryHandler handler =
            new GetPackagingComponentsQueryHandler(_repoMock.Object);

        // Act
        GetPackagingComponentsResponse response =
            await handler.Handle(new GetPackagingComponentsQuery(), CancellationToken.None);

        // Assert
        response.Items.Should().HaveCount(2);
        response.Items.Should().OnlyContain(i => i.ActivePrice != null && i.ActivePrice.PricePerUnit == 12.50m);
    }

    [Fact]
    public async Task GetPackagingComponent_ExistingId_ReturnsMappedResponseWithActivePrice()
    {
        // Arrange
        PackagingComponent component = BuildComponent();
        PackagingComponentPrice activePrice = component.Prices.First();

        _repoMock
            .Setup(r => r.GetWithPricesByIdAsync(component.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(component);

        GetPackagingComponentQueryHandler handler =
            new GetPackagingComponentQueryHandler(_repoMock.Object);

        // Act
        GetPackagingComponentResponse? response =
            await handler.Handle(
                new GetPackagingComponentQuery { Id = component.Id },
                CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response!.Name.Should().Be(component.Name);
        response.ActivePrice.Should().NotBeNull();
        response.ActivePrice!.SupplierId.Should().Be(activePrice.SupplierId);
        response.ActivePrice.PricePerUnit.Should().Be(activePrice.PricePerUnit);
    }

    [Fact]
    public async Task GetPackagingComponent_NoActivePrice_ReturnsNullActivePrice()
    {
        // Arrange
        PackagingComponent component = BuildComponent(withActivePrice: false);

        _repoMock
            .Setup(r => r.GetWithPricesByIdAsync(component.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(component);

        GetPackagingComponentQueryHandler handler =
            new GetPackagingComponentQueryHandler(_repoMock.Object);

        // Act
        GetPackagingComponentResponse? response =
            await handler.Handle(
                new GetPackagingComponentQuery { Id = component.Id },
                CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response!.ActivePrice.Should().BeNull();
    }

    [Fact]
    public async Task GetPackagingComponent_NotFound_ReturnsNull()
    {
        // Arrange
        _repoMock
            .Setup(r => r.GetWithPricesByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PackagingComponent?)null);

        GetPackagingComponentQueryHandler handler =
            new GetPackagingComponentQueryHandler(_repoMock.Object);

        // Act
        GetPackagingComponentResponse? response =
            await handler.Handle(
                new GetPackagingComponentQuery { Id = Guid.NewGuid() },
                CancellationToken.None);

        // Assert
        response.Should().BeNull();
    }
}
