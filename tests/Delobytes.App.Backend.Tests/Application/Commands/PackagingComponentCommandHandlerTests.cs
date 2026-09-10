using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Catalog.Application.Commands.PackagingComponents.CreatePackagingComponent;
using Delobytes.App.Backend.Catalog.Application.Commands.PackagingComponents.DeletePackagingComponent;
using Delobytes.App.Backend.Catalog.Application.Commands.PackagingComponents.UpdatePackagingComponent;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
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

    private static readonly Guid _supplierId = Guid.NewGuid();

    private static PackagingComponent BuildComponent(Guid? id = null)
        => new PackagingComponent
        {
            Id = id ?? Guid.NewGuid(),
            Name = "Коробка 20x15x10",
            Unit = Unit.Piece,
            PricePerUnit = 12.50m,
            SupplierId = _supplierId,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    // ── Create ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreatePackagingComponent_ValidCommand_AddsAndSavesEntity()
    {
        // Arrange
        _repoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        CreatePackagingComponentCommandHandler handler =
            new CreatePackagingComponentCommandHandler(_repoMock.Object);

        Guid supplierId = Guid.NewGuid();

        CreatePackagingComponentCommand command = new CreatePackagingComponentCommand
        {
            Name = "Пузырчатая плёнка",
            Description = "Рулон 1м x 50м",
            Unit = Unit.Meter,
            PricePerUnit = 350m,
            SupplierId = supplierId,
        };

        // Act
        CreatePackagingComponentResponse response =
            await handler.Handle(command, CancellationToken.None);

        // Assert
        response.Id.Should().NotBe(Guid.Empty);

        _repoMock.Verify(
            r => r.Add(It.Is<PackagingComponent>(c =>
                c.Name == "Пузырчатая плёнка" &&
                c.Unit == Unit.Meter &&
                c.PricePerUnit == 350m &&
                c.SupplierId == supplierId &&
                c.IsActive == true)),
            Times.Once);

        _repoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreatePackagingComponent_NullableFieldsArePreserved()
    {
        // Arrange
        PackagingComponent? captured = null;

        _repoMock
            .Setup(r => r.Add(It.IsAny<PackagingComponent>()))
            .Callback<PackagingComponent>(c => captured = c);

        _repoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        CreatePackagingComponentCommandHandler handler =
            new CreatePackagingComponentCommandHandler(_repoMock.Object);

        CreatePackagingComponentCommand command = new CreatePackagingComponentCommand
        {
            Name = "Скотч",
            Unit = Unit.Piece,
            PricePerUnit = 25m,
            Description = null,
            SupplierId = null,
        };

        // Act
        await handler.Handle(command, CancellationToken.None);

        // Assert
        captured.Should().NotBeNull();
        captured!.Description.Should().BeNull();
        captured.SupplierId.Should().BeNull();
    }

    // ── Update ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdatePackagingComponent_ExistingId_UpdatesFields()
    {
        // Arrange
        PackagingComponent existing = BuildComponent();

        _repoMock
            .Setup(r => r.GetByIdAsync(existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        _repoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        UpdatePackagingComponentCommandHandler handler =
            new UpdatePackagingComponentCommandHandler(_repoMock.Object);

        Guid newSupplierId = Guid.NewGuid();

        UpdatePackagingComponentCommand command = new UpdatePackagingComponentCommand
        {
            Id = existing.Id,
            Name = "Коробка 30x20x15",
            Unit = Unit.Piece,
            PricePerUnit = 18m,
            SupplierId = newSupplierId,
            IsActive = true,
        };

        // Act
        UpdatePackagingComponentResponse response =
            await handler.Handle(command, CancellationToken.None);

        // Assert
        response.Found.Should().BeTrue();
        existing.Name.Should().Be("Коробка 30x20x15");
        existing.PricePerUnit.Should().Be(18m);
        existing.SupplierId.Should().Be(newSupplierId);
        existing.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task UpdatePackagingComponent_ClearSupplier_SetsSuppliierIdToNull()
    {
        // Arrange
        PackagingComponent existing = BuildComponent();

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
            Name = existing.Name,
            Unit = existing.Unit,
            PricePerUnit = existing.PricePerUnit,
            SupplierId = null,
            IsActive = true,
        };

        // Act
        await handler.Handle(command, CancellationToken.None);

        // Assert
        existing.SupplierId.Should().BeNull();
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
            PricePerUnit = 1m,
            IsActive = true,
        };

        // Act
        UpdatePackagingComponentResponse response =
            await handler.Handle(command, CancellationToken.None);

        // Assert
        response.Found.Should().BeFalse();
        _repoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Delete ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeletePackagingComponent_ExistingId_SoftDeletes()
    {
        // Arrange
        PackagingComponent existing = BuildComponent();

        _repoMock
            .Setup(r => r.GetByIdAsync(existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        _repoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        DeletePackagingComponentCommandHandler handler =
            new DeletePackagingComponentCommandHandler(_repoMock.Object);

        // Act
        DeletePackagingComponentResponse response =
            await handler.Handle(
                new DeletePackagingComponentCommand { Id = existing.Id },
                CancellationToken.None);

        // Assert
        response.Found.Should().BeTrue();
        existing.IsActive.Should().BeFalse();
        _repoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeletePackagingComponent_NotFound_ReturnsFalse()
    {
        // Arrange
        _repoMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PackagingComponent?)null);

        DeletePackagingComponentCommandHandler handler =
            new DeletePackagingComponentCommandHandler(_repoMock.Object);

        // Act
        DeletePackagingComponentResponse response =
            await handler.Handle(
                new DeletePackagingComponentCommand { Id = Guid.NewGuid() },
                CancellationToken.None);

        // Assert
        response.Found.Should().BeFalse();
        _repoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Query handlers ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetPackagingComponents_ReturnsAllItems()
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
    }

    [Fact]
    public async Task GetPackagingComponent_ExistingId_ReturnsMappedResponse()
    {
        // Arrange
        Guid supplierId = Guid.NewGuid();
        PackagingComponent component = new PackagingComponent
        {
            Id = Guid.NewGuid(),
            Name = "Коробка",
            Unit = Unit.Piece,
            PricePerUnit = 10m,
            SupplierId = supplierId,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _repoMock
            .Setup(r => r.GetByIdAsync(component.Id, It.IsAny<CancellationToken>()))
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
        response!.SupplierId.Should().Be(supplierId);
        response.Name.Should().Be("Коробка");
    }

    [Fact]
    public async Task GetPackagingComponent_NotFound_ReturnsNull()
    {
        // Arrange
        _repoMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
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
