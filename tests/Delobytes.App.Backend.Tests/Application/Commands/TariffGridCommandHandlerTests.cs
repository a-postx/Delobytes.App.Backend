using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Catalog.Application.Commands.TariffGrids.CreateTariffGrid;
using Delobytes.App.Backend.Catalog.Application.Commands.TariffGrids.DeleteTariffGrid;
using Delobytes.App.Backend.Catalog.Application.Commands.TariffGrids.UpdateTariffGrid;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Application.Queries.TariffGrids.GetTariffGrid;
using Delobytes.App.Backend.Catalog.Application.Queries.TariffGrids.GetTariffGrids;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using FluentAssertions;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Catalog;

public class TariffGridCommandHandlerTests
{
    private readonly Mock<ITariffGridRepository> _repoMock = new();

    private static TariffGrid BuildGrid(Guid? id = null, TariffType type = TariffType.WbLogistics)
        => new TariffGrid
        {
            Id = id ?? Guid.NewGuid(),
            Name = "WB Тариф 2026",
            TariffType = type,
            ValidFrom = new DateOnly(2026, 1, 1),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            Entries = new List<TariffGridEntry>(),
        };

    // ── Create ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateTariffGrid_WithEntries_AddsGridAndAllEntries()
    {
        // Arrange
        _repoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        CreateTariffGridCommandHandler handler =
            new CreateTariffGridCommandHandler(_repoMock.Object);

        CreateTariffGridCommand command = new CreateTariffGridCommand
        {
            Name = "WB Центральный 2026",
            TariffType = TariffType.WbLogistics,
            ValidFrom = new DateOnly(2026, 3, 1),
            ChannelId = null,
            Entries = new List<TariffGridEntryRequest>
            {
                new TariffGridEntryRequest { RegionOrCity = "Москва", Rate = 75m },
                new TariffGridEntryRequest { RegionOrCity = "СПб", Rate = 90m },
            },
        };

        // Act
        CreateTariffGridResponse response =
            await handler.Handle(command, CancellationToken.None);

        // Assert
        response.Id.Should().NotBe(Guid.Empty);

        _repoMock.Verify(
            r => r.Add(It.Is<TariffGrid>(g =>
                g.Name == "WB Центральный 2026" &&
                g.TariffType == TariffType.WbLogistics &&
                g.IsActive == true &&
                g.Entries.Count == 2)),
            Times.Once);

        _repoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateTariffGrid_FulfillmentType_PreservesVolumeThresholds()
    {
        // Arrange
        TariffGrid? captured = null;

        _repoMock
            .Setup(r => r.Add(It.IsAny<TariffGrid>()))
            .Callback<TariffGrid>(g => captured = g);

        _repoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        CreateTariffGridCommandHandler handler =
            new CreateTariffGridCommandHandler(_repoMock.Object);

        CreateTariffGridCommand command = new CreateTariffGridCommand
        {
            Name = "ФФ Казань 2026",
            TariffType = TariffType.FulfillmentCenter,
            ValidFrom = new DateOnly(2026, 1, 1),
            Entries = new List<TariffGridEntryRequest>
            {
                new TariffGridEntryRequest { RegionOrCity = "Казань", VolumeThresholdLiters = 1.0m, Rate = 50m },
                new TariffGridEntryRequest { RegionOrCity = "Казань", VolumeThresholdLiters = 5.0m, Rate = 45m },
                new TariffGridEntryRequest { RegionOrCity = "Казань", VolumeThresholdLiters = null, Rate = 40m },
            },
        };

        // Act
        await handler.Handle(command, CancellationToken.None);

        // Assert
        captured.Should().NotBeNull();
        captured!.Entries.Should().HaveCount(3);
        captured.Entries.Should().Contain(e => e.VolumeThresholdLiters == null);
    }

    [Fact]
    public async Task CreateTariffGrid_EmptyEntries_CreatesGridWithNoRows()
    {
        // Arrange
        TariffGrid? captured = null;

        _repoMock
            .Setup(r => r.Add(It.IsAny<TariffGrid>()))
            .Callback<TariffGrid>(g => captured = g);

        _repoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        CreateTariffGridCommandHandler handler =
            new CreateTariffGridCommandHandler(_repoMock.Object);

        // Act
        await handler.Handle(new CreateTariffGridCommand
        {
            Name = "Пустая сетка",
            TariffType = TariffType.WbLogistics,
            ValidFrom = new DateOnly(2026, 6, 1),
            Entries = new List<TariffGridEntryRequest>(),
        }, CancellationToken.None);

        // Assert
        captured!.Entries.Should().BeEmpty();
    }

    // ── Update ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateTariffGrid_ExistingGrid_UpdatesNameAndActiveStatus()
    {
        // Arrange
        Guid id = Guid.NewGuid();
        TariffGrid existing = BuildGrid(id);

        _repoMock
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        _repoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        UpdateTariffGridCommandHandler handler =
            new UpdateTariffGridCommandHandler(_repoMock.Object);

        // Act
        UpdateTariffGridResponse response = await handler.Handle(
            new UpdateTariffGridCommand { Id = id, Name = "WB Новый тариф", IsActive = false },
            CancellationToken.None);

        // Assert
        response.Found.Should().BeTrue();
        existing.Name.Should().Be("WB Новый тариф");
        existing.IsActive.Should().BeFalse();

        _repoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateTariffGrid_NotFound_ReturnsFalseWithoutSaving()
    {
        // Arrange
        _repoMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TariffGrid?)null);

        UpdateTariffGridCommandHandler handler =
            new UpdateTariffGridCommandHandler(_repoMock.Object);

        // Act
        UpdateTariffGridResponse response = await handler.Handle(
            new UpdateTariffGridCommand { Id = Guid.NewGuid(), Name = "Х", IsActive = true },
            CancellationToken.None);

        // Assert
        response.Found.Should().BeFalse();
        _repoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Delete ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteTariffGrid_ExistingGrid_DeactivatesAndReturnsFound()
    {
        // Arrange
        Guid id = Guid.NewGuid();
        TariffGrid existing = BuildGrid(id);

        _repoMock
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        _repoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        DeleteTariffGridCommandHandler handler =
            new DeleteTariffGridCommandHandler(_repoMock.Object);

        // Act
        DeleteTariffGridResponse response = await handler.Handle(
            new DeleteTariffGridCommand { Id = id }, CancellationToken.None);

        // Assert
        response.Found.Should().BeTrue();
        existing.IsActive.Should().BeFalse();

        _repoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteTariffGrid_NotFound_ReturnsFalseWithoutSaving()
    {
        // Arrange
        _repoMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TariffGrid?)null);

        DeleteTariffGridCommandHandler handler =
            new DeleteTariffGridCommandHandler(_repoMock.Object);

        // Act
        DeleteTariffGridResponse response = await handler.Handle(
            new DeleteTariffGridCommand { Id = Guid.NewGuid() }, CancellationToken.None);

        // Assert
        response.Found.Should().BeFalse();
        _repoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Queries ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetTariffGrid_ExistingId_ReturnsMappedResponseWithEntries()
    {
        // Arrange
        Guid id = Guid.NewGuid();
        TariffGrid grid = BuildGrid(id);

        grid.Entries.Add(new TariffGridEntry
        {
            Id = Guid.NewGuid(),
            TariffGridId = id,
            RegionOrCity = "Москва",
            Rate = 75m,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        _repoMock
            .Setup(r => r.GetByIdWithEntriesAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(grid);

        GetTariffGridQueryHandler handler =
            new GetTariffGridQueryHandler(_repoMock.Object);

        // Act
        GetTariffGridResponse? response = await handler.Handle(
            new GetTariffGridQuery { Id = id }, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response!.Id.Should().Be(id);
        response.TariffType.Should().Be(TariffType.WbLogistics);
        response.Entries.Should().HaveCount(1);
        response.Entries[0].RegionOrCity.Should().Be("Москва");
        response.Entries[0].Rate.Should().Be(75m);
    }

    [Fact]
    public async Task GetTariffGrid_MissingId_ReturnsNull()
    {
        // Arrange
        _repoMock
            .Setup(r => r.GetByIdWithEntriesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TariffGrid?)null);

        GetTariffGridQueryHandler handler =
            new GetTariffGridQueryHandler(_repoMock.Object);

        // Act
        GetTariffGridResponse? response = await handler.Handle(
            new GetTariffGridQuery { Id = Guid.NewGuid() }, CancellationToken.None);

        // Assert
        response.Should().BeNull();
    }

    [Fact]
    public async Task GetTariffGrids_WithTypeFilter_CallsGetByType()
    {
        // Arrange
        List<TariffGrid> grids = new List<TariffGrid>
        {
            BuildGrid(type: TariffType.FulfillmentCenter),
            BuildGrid(type: TariffType.FulfillmentCenter),
        };

        _repoMock
            .Setup(r => r.GetByTypeAsync(TariffType.FulfillmentCenter, It.IsAny<CancellationToken>()))
            .ReturnsAsync(grids);

        GetTariffGridsQueryHandler handler =
            new GetTariffGridsQueryHandler(_repoMock.Object);

        // Act
        GetTariffGridsResponse response = await handler.Handle(
            new GetTariffGridsQuery { TariffType = TariffType.FulfillmentCenter },
            CancellationToken.None);

        // Assert
        response.Items.Should().HaveCount(2);
        response.Items.Should().OnlyContain(i => i.TariffType == TariffType.FulfillmentCenter);

        _repoMock.Verify(r => r.GetByTypeAsync(TariffType.FulfillmentCenter, It.IsAny<CancellationToken>()), Times.Once);
        _repoMock.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetTariffGrids_WithoutFilter_CallsGetAll()
    {
        // Arrange
        List<TariffGrid> grids = new List<TariffGrid>
        {
            BuildGrid(type: TariffType.WbLogistics),
            BuildGrid(type: TariffType.FulfillmentCenter),
        };

        _repoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(grids);

        GetTariffGridsQueryHandler handler =
            new GetTariffGridsQueryHandler(_repoMock.Object);

        // Act
        GetTariffGridsResponse response = await handler.Handle(
            new GetTariffGridsQuery { TariffType = null },
            CancellationToken.None);

        // Assert
        response.Items.Should().HaveCount(2);

        _repoMock.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Times.Once);
        _repoMock.Verify(r => r.GetByTypeAsync(It.IsAny<TariffType>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
