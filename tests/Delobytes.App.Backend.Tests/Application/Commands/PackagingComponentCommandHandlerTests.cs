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

    private static PackagingComponent BuildComponent(Guid? id = null)
        => new PackagingComponent
        {
            Id = id ?? Guid.NewGuid(),
            Name = "Коробка 20x15x10",
            Unit = Unit.Piece,
            PricePerUnit = 12.50m,
            Supplier = "ООО Упаковка",
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

        CreatePackagingComponentCommand command = new CreatePackagingComponentCommand
        {
            Name = "Пузырчатая плёнка",
            Description = "Рулон 1м x 50м",
            Unit = Unit.Meter,
            PricePerUnit = 350m,
            Supplier = "ИП Пузырьков",
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
            Supplier = null,
        };

        // Act
        await handler.Handle(command, CancellationToken.None);

        // Assert
        captured.Should().NotBeNull();
        captured!.Description.Should().BeNull();
        captured.Supplier.Should().BeNull();
    }

    // ── Update ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdatePackagingComponent_ExistingComponent_UpdatesFieldsAndReturnsFound()
    {
        // Arrange
        Guid id = Guid.NewGuid();
        PackagingComponent existing = BuildComponent(id);

        _repoMock
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        _repoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        UpdatePackagingComponentCommandHandler handler =
            new UpdatePackagingComponentCommandHandler(_repoMock.Object);

        UpdatePackagingComponentCommand command = new UpdatePackagingComponentCommand
        {
            Id = id,
            Name = "Коробка 25x20x15",
            Unit = Unit.Piece,
            PricePerUnit = 18m,
            Supplier = "ООО НоваяУпаковка",
            IsActive = false,
        };

        // Act
        UpdatePackagingComponentResponse response =
            await handler.Handle(command, CancellationToken.None);

        // Assert
        response.Found.Should().BeTrue();
        existing.Name.Should().Be("Коробка 25x20x15");
        existing.PricePerUnit.Should().Be(18m);
        existing.IsActive.Should().BeFalse();
        existing.UpdatedAt.Should().NotBeNull();

        _repoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdatePackagingComponent_NotFound_ReturnsFalseWithoutSaving()
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
            Name = "Х",
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
    public async Task DeletePackagingComponent_ExistingComponent_SoftDeletesAndReturnsFound()
    {
        // Arrange
        Guid id = Guid.NewGuid();
        PackagingComponent existing = BuildComponent(id);

        _repoMock
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        _repoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        DeletePackagingComponentCommandHandler handler =
            new DeletePackagingComponentCommandHandler(_repoMock.Object);

        // Act
        DeletePackagingComponentResponse response =
            await handler.Handle(new DeletePackagingComponentCommand { Id = id }, CancellationToken.None);

        // Assert
        response.Found.Should().BeTrue();
        existing.IsActive.Should().BeFalse();
        existing.UpdatedAt.Should().NotBeNull();

        _repoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeletePackagingComponent_NotFound_ReturnsFalseWithoutSaving()
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

    // ── Get / GetAll ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetPackagingComponent_ExistingId_ReturnsMappedResponse()
    {
        // Arrange
        PackagingComponent component = BuildComponent();
        component.Description = "Тест описание";

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
        response!.Id.Should().Be(component.Id);
        response.Name.Should().Be(component.Name);
        response.Unit.Should().Be(component.Unit);
        response.PricePerUnit.Should().Be(component.PricePerUnit);
        response.Description.Should().Be("Тест описание");
        response.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task GetPackagingComponent_MissingId_ReturnsNull()
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

    [Fact]
    public async Task GetPackagingComponents_ReturnsAllMappedItems()
    {
        // Arrange
        List<PackagingComponent> components = new List<PackagingComponent>
        {
            BuildComponent(),
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
        response.Items.Should().HaveCount(3);
        response.Items.Should().OnlyContain(i => i.Id != Guid.Empty);
    }

    [Fact]
    public async Task GetPackagingComponents_EmptyRepository_ReturnsEmptyList()
    {
        // Arrange
        _repoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PackagingComponent>());

        GetPackagingComponentsQueryHandler handler =
            new GetPackagingComponentsQueryHandler(_repoMock.Object);

        // Act
        GetPackagingComponentsResponse response =
            await handler.Handle(new GetPackagingComponentsQuery(), CancellationToken.None);

        // Assert
        response.Items.Should().BeEmpty();
    }
}
