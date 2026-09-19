using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Catalog.Application.Commands.CostTypes.CreateCostType;
using Delobytes.App.Backend.Catalog.Application.Commands.CostTypes.UpdateCostType;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Application.Queries.CostTypes.GetCostTypes;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using FluentAssertions;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Catalog;

public class CostTypeCommandHandlerTests
{
    private readonly Mock<ICostTypeRepository> _repoMock = new();

    private static CostType BuildCostType(Guid? id = null, bool isActive = true)
        => new CostType
        {
            Id = id ?? Guid.NewGuid(),
            Name = "Логистика до покупателя",
            Description = "Стоимость доставки от склада до покупателя",
            IsActive = isActive,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    // ── Create ────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateCostType_ValidCommand_AddsEntityAndReturnsId()
    {
        // Arrange
        _repoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        CreateCostTypeCommandHandler handler =
            new CreateCostTypeCommandHandler(_repoMock.Object);

        CreateCostTypeCommand command = new CreateCostTypeCommand
        {
            Name = "Фулфилмент",
            Description = "Комиссия фулфилмент-центра",
        };

        // Act
        CreateCostTypeResponse response =
            await handler.Handle(command, CancellationToken.None);

        // Assert
        response.Id.Should().NotBe(Guid.Empty);

        _repoMock.Verify(
            r => r.Add(It.Is<CostType>(ct =>
                ct.Name == "Фулфилмент" &&
                ct.Description == "Комиссия фулфилмент-центра" &&
                ct.IsActive == true)),
            Times.Once);

        _repoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateCostType_NullDescription_AddsEntityWithNullDescription()
    {
        // Arrange
        CostType? captured = null;

        _repoMock
            .Setup(r => r.Add(It.IsAny<CostType>()))
            .Callback<CostType>(ct => captured = ct);

        _repoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        CreateCostTypeCommandHandler handler =
            new CreateCostTypeCommandHandler(_repoMock.Object);

        CreateCostTypeCommand command = new CreateCostTypeCommand
        {
            Name = "Упаковка",
            Description = null,
        };

        // Act
        await handler.Handle(command, CancellationToken.None);

        // Assert
        captured.Should().NotBeNull();
        captured!.Name.Should().Be("Упаковка");
        captured.Description.Should().BeNull();
        captured.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task CreateCostType_CreatedAtIsSetToUtcNow()
    {
        // Arrange
        CostType? captured = null;
        DateTimeOffset before = DateTimeOffset.UtcNow;

        _repoMock
            .Setup(r => r.Add(It.IsAny<CostType>()))
            .Callback<CostType>(ct => captured = ct);

        _repoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        CreateCostTypeCommandHandler handler =
            new CreateCostTypeCommandHandler(_repoMock.Object);

        // Act
        await handler.Handle(new CreateCostTypeCommand { Name = "Тест" }, CancellationToken.None);

        DateTimeOffset after = DateTimeOffset.UtcNow;

        // Assert
        captured!.CreatedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    // ── Update ────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateCostType_ExistingId_UpdatesAllFieldsAndReturnsFound()
    {
        // Arrange
        CostType existing = BuildCostType();

        _repoMock
            .Setup(r => r.GetByIdAsync(existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        _repoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        UpdateCostTypeCommandHandler handler =
            new UpdateCostTypeCommandHandler(_repoMock.Object);

        UpdateCostTypeCommand command = new UpdateCostTypeCommand
        {
            Id = existing.Id,
            Name = "Доставка FBS",
            Description = "Обновлённое описание",
            IsActive = true,
        };

        // Act
        UpdateCostTypeResponse response =
            await handler.Handle(command, CancellationToken.None);

        // Assert
        response.Found.Should().BeTrue();
        existing.Name.Should().Be("Доставка FBS");
        existing.Description.Should().Be("Обновлённое описание");
        existing.IsActive.Should().BeTrue();
        existing.UpdatedAt.Should().NotBeNull();

        _repoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateCostType_Deactivate_SetsIsActiveToFalse()
    {
        // Arrange
        CostType existing = BuildCostType();

        _repoMock
            .Setup(r => r.GetByIdAsync(existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        _repoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        UpdateCostTypeCommandHandler handler =
            new UpdateCostTypeCommandHandler(_repoMock.Object);

        // Act
        UpdateCostTypeResponse response = await handler.Handle(
            new UpdateCostTypeCommand
            {
                Id = existing.Id,
                Name = existing.Name,
                Description = existing.Description,
                IsActive = false,
            },
            CancellationToken.None);

        // Assert
        response.Found.Should().BeTrue();
        existing.IsActive.Should().BeFalse();
        existing.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task UpdateCostType_NotFound_ReturnsFalseWithoutSaving()
    {
        // Arrange
        _repoMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CostType?)null);

        UpdateCostTypeCommandHandler handler =
            new UpdateCostTypeCommandHandler(_repoMock.Object);

        // Act
        UpdateCostTypeResponse response = await handler.Handle(
            new UpdateCostTypeCommand
            {
                Id = Guid.NewGuid(),
                Name = "Несуществующий",
                IsActive = true,
            },
            CancellationToken.None);

        // Assert
        response.Found.Should().BeFalse();
        _repoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateCostType_NullDescription_ClearsDescription()
    {
        // Arrange
        CostType existing = BuildCostType();
        existing.Description = "Старое описание";

        _repoMock
            .Setup(r => r.GetByIdAsync(existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        _repoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        UpdateCostTypeCommandHandler handler =
            new UpdateCostTypeCommandHandler(_repoMock.Object);

        // Act
        await handler.Handle(
            new UpdateCostTypeCommand
            {
                Id = existing.Id,
                Name = existing.Name,
                Description = null,
                IsActive = true,
            },
            CancellationToken.None);

        // Assert
        existing.Description.Should().BeNull();
    }

    // ── GetAll query ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetCostTypes_MultipleCostTypes_ReturnsMappedList()
    {
        // Arrange
        List<CostType> costTypes = new List<CostType>
        {
            BuildCostType(),
            BuildCostType(),
        };

        _repoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(costTypes);

        GetCostTypesQueryHandler handler =
            new GetCostTypesQueryHandler(_repoMock.Object);

        // Act
        GetCostTypesResponse response =
            await handler.Handle(new GetCostTypesQuery(), CancellationToken.None);

        // Assert
        response.Items.Should().HaveCount(2);
        response.Items.Should().OnlyContain(i => i.Name == "Логистика до покупателя");
    }

    [Fact]
    public async Task GetCostTypes_EmptyRepository_ReturnsEmptyList()
    {
        // Arrange
        _repoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CostType>());

        GetCostTypesQueryHandler handler =
            new GetCostTypesQueryHandler(_repoMock.Object);

        // Act
        GetCostTypesResponse response =
            await handler.Handle(new GetCostTypesQuery(), CancellationToken.None);

        // Assert
        response.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task GetCostTypes_MapsAllFields()
    {
        // Arrange
        Guid id = Guid.NewGuid();
        CostType costType = BuildCostType(id);

        _repoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CostType> { costType });

        GetCostTypesQueryHandler handler =
            new GetCostTypesQueryHandler(_repoMock.Object);

        // Act
        GetCostTypesResponse response =
            await handler.Handle(new GetCostTypesQuery(), CancellationToken.None);

        // Assert
        CostTypeItem item = response.Items.Single();
        item.Id.Should().Be(id);
        item.Name.Should().Be(costType.Name);
        item.Description.Should().Be(costType.Description);
        item.IsActive.Should().BeTrue();
    }
}
