using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Catalog.Application.Commands.WorkRates.CreateWorkRate;
using Delobytes.App.Backend.Catalog.Application.Commands.WorkRates.DeleteWorkRate;
using Delobytes.App.Backend.Catalog.Application.Commands.WorkRates.UpdateWorkRate;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Application.Queries.WorkRates.GetWorkRate;
using Delobytes.App.Backend.Catalog.Application.Queries.WorkRates.GetWorkRates;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using FluentAssertions;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Catalog;

public class WorkRateCommandHandlerTests
{
    private readonly Mock<IWorkRateRepository> _repoMock = new();

    private static WorkRate BuildWorkRate(Guid? id = null)
        => new WorkRate
        {
            Id = id ?? Guid.NewGuid(),
            Name = "Базовая ставка",
            DailyWage = 3000m,
            ValidFrom = new DateOnly(2026, 1, 1),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    // ── Create ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateWorkRate_ValidCommand_AddsEntityAndReturnsId()
    {
        // Arrange
        _repoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        CreateWorkRateCommandHandler handler =
            new CreateWorkRateCommandHandler(_repoMock.Object);

        CreateWorkRateCommand command = new CreateWorkRateCommand
        {
            Name = "Ставка Q2 2026",
            DailyWage = 3500m,
            ValidFrom = new DateOnly(2026, 4, 1),
        };

        // Act
        CreateWorkRateResponse response =
            await handler.Handle(command, CancellationToken.None);

        // Assert
        response.Id.Should().NotBe(Guid.Empty);

        _repoMock.Verify(
            r => r.Add(It.Is<WorkRate>(wr =>
                wr.Name == "Ставка Q2 2026" &&
                wr.DailyWage == 3500m &&
                wr.ValidFrom == new DateOnly(2026, 4, 1) &&
                wr.IsActive == true)),
            Times.Once);

        _repoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Delete ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteWorkRate_ExistingRate_SoftDeletesAndReturnsFound()
    {
        // Arrange
        Guid id = Guid.NewGuid();
        WorkRate existing = BuildWorkRate(id);

        _repoMock
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        _repoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        DeleteWorkRateCommandHandler handler =
            new DeleteWorkRateCommandHandler(_repoMock.Object);

        // Act
        DeleteWorkRateResponse response = await handler.Handle(
            new DeleteWorkRateCommand { Id = id }, CancellationToken.None);

        // Assert
        response.Found.Should().BeTrue();
        existing.IsActive.Should().BeFalse();
        existing.UpdatedAt.Should().NotBeNull();

        _repoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteWorkRate_NotFound_ReturnsFalseWithoutSaving()
    {
        // Arrange
        _repoMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkRate?)null);

        DeleteWorkRateCommandHandler handler =
            new DeleteWorkRateCommandHandler(_repoMock.Object);

        // Act
        DeleteWorkRateResponse response = await handler.Handle(
            new DeleteWorkRateCommand { Id = Guid.NewGuid() }, CancellationToken.None);

        // Assert
        response.Found.Should().BeFalse();
        _repoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Queries ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetWorkRate_ExistingId_ReturnsMappedResponse()
    {
        // Arrange
        WorkRate rate = BuildWorkRate();

        _repoMock
            .Setup(r => r.GetByIdAsync(rate.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rate);

        GetWorkRateQueryHandler handler =
            new GetWorkRateQueryHandler(_repoMock.Object);

        // Act
        GetWorkRateResponse? response = await handler.Handle(
            new GetWorkRateQuery { Id = rate.Id }, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response!.Id.Should().Be(rate.Id);
        response.Name.Should().Be(rate.Name);
        response.DailyWage.Should().Be(rate.DailyWage);
        response.ValidFrom.Should().Be(rate.ValidFrom);
        response.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task GetWorkRate_MissingId_ReturnsNull()
    {
        // Arrange
        _repoMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkRate?)null);

        GetWorkRateQueryHandler handler =
            new GetWorkRateQueryHandler(_repoMock.Object);

        // Act
        GetWorkRateResponse? response = await handler.Handle(
            new GetWorkRateQuery { Id = Guid.NewGuid() }, CancellationToken.None);

        // Assert
        response.Should().BeNull();
    }

    [Fact]
    public async Task GetWorkRates_MultipleRates_ReturnsMappedList()
    {
        // Arrange
        List<WorkRate> rates = new List<WorkRate>
        {
            BuildWorkRate(),
            BuildWorkRate(),
        };

        _repoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(rates);

        GetWorkRatesQueryHandler handler =
            new GetWorkRatesQueryHandler(_repoMock.Object);

        // Act
        GetWorkRatesResponse response =
            await handler.Handle(new GetWorkRatesQuery(), CancellationToken.None);

        // Assert
        response.Items.Should().HaveCount(2);
        response.Items.Should().OnlyContain(i => i.DailyWage == 3000m);
    }
}
