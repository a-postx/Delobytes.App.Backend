using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Catalog.Application.Commands.WorkRates.UpdateWorkRate;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using FluentAssertions;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Catalog;

public class WorkRateUpdateTests
{
    private readonly Mock<IWorkRateRepository> _repoMock = new();

    private static WorkRate BuildWorkRate(Guid? id = null, bool isActive = true)
        => new WorkRate
        {
            Id = id ?? Guid.NewGuid(),
            Name = "Базовая ставка",
            DailyWage = 3000m,
            ValidFrom = new DateOnly(2026, 1, 1),
            IsActive = isActive,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    // ── Update (IsActive Toggle) ───────────────────────────────────────────────────

    [Fact]
    public async Task UpdateWorkRate_ExistingRate_TogglesIsActive()
    {
        // Arrange
        WorkRate existing = BuildWorkRate(isActive: true);

        _repoMock
            .Setup(r => r.GetByIdAsync(existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        _repoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        UpdateWorkRateCommandHandler handler =
            new UpdateWorkRateCommandHandler(_repoMock.Object);

        UpdateWorkRateCommand command = new UpdateWorkRateCommand
        {
            Id = existing.Id,
            IsActive = false,
        };

        // Act
        UpdateWorkRateResponse response =
            await handler.Handle(command, CancellationToken.None);

        // Assert
        response.Found.Should().BeTrue();
        existing.IsActive.Should().BeFalse();
        existing.UpdatedAt.Should().NotBeNull();

        _repoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateWorkRate_CanReactivate()
    {
        // Arrange
        WorkRate existing = BuildWorkRate(isActive: false);
        existing.UpdatedAt = DateTimeOffset.UtcNow.AddDays(-10);

        _repoMock
            .Setup(r => r.GetByIdAsync(existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        _repoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        UpdateWorkRateCommandHandler handler =
            new UpdateWorkRateCommandHandler(_repoMock.Object);

        UpdateWorkRateCommand command = new UpdateWorkRateCommand
        {
            Id = existing.Id,
            IsActive = true,
        };

        DateTimeOffset beforeUpdate = DateTimeOffset.UtcNow;

        // Act
        UpdateWorkRateResponse response =
            await handler.Handle(command, CancellationToken.None);

        // Assert
        response.Found.Should().BeTrue();
        existing.IsActive.Should().BeTrue();
        existing.UpdatedAt.Should().BeOnOrAfter(beforeUpdate);

        _repoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateWorkRate_NotFound_ReturnsFalseWithoutSaving()
    {
        // Arrange
        _repoMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkRate?)null);

        UpdateWorkRateCommandHandler handler =
            new UpdateWorkRateCommandHandler(_repoMock.Object);

        UpdateWorkRateCommand command = new UpdateWorkRateCommand
        {
            Id = Guid.NewGuid(),
            IsActive = false,
        };

        // Act
        UpdateWorkRateResponse response =
            await handler.Handle(command, CancellationToken.None);

        // Assert
        response.Found.Should().BeFalse();
        _repoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateWorkRate_IdempotentUpdate_StillUpdatesTimestamp()
    {
        // Arrange
        WorkRate existing = BuildWorkRate(isActive: true);
        DateTimeOffset oldUpdatedAt = DateTimeOffset.UtcNow.AddHours(-1);
        existing.UpdatedAt = oldUpdatedAt;

        _repoMock
            .Setup(r => r.GetByIdAsync(existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        _repoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        UpdateWorkRateCommandHandler handler =
            new UpdateWorkRateCommandHandler(_repoMock.Object);

        UpdateWorkRateCommand command = new UpdateWorkRateCommand
        {
            Id = existing.Id,
            IsActive = true,
        };

        DateTimeOffset beforeCall = DateTimeOffset.UtcNow;

        // Act
        UpdateWorkRateResponse response =
            await handler.Handle(command, CancellationToken.None);

        // Assert
        response.Found.Should().BeTrue();
        existing.IsActive.Should().BeTrue();
        existing.UpdatedAt.Should().BeOnOrAfter(beforeCall);
        existing.UpdatedAt.Should().BeAfter(oldUpdatedAt);
    }

    [Fact]
    public async Task UpdateWorkRate_PreservesOtherFields()
    {
        // Arrange
        WorkRate existing = BuildWorkRate();
        string originalName = existing.Name;
        decimal originalWage = existing.DailyWage;
        DateOnly originalValidFrom = existing.ValidFrom;

        _repoMock
            .Setup(r => r.GetByIdAsync(existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        _repoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        UpdateWorkRateCommandHandler handler =
            new UpdateWorkRateCommandHandler(_repoMock.Object);

        UpdateWorkRateCommand command = new UpdateWorkRateCommand
        {
            Id = existing.Id,
            IsActive = false,
        };

        // Act
        await handler.Handle(command, CancellationToken.None);

        // Assert
        existing.Name.Should().Be(originalName);
        existing.DailyWage.Should().Be(originalWage);
        existing.ValidFrom.Should().Be(originalValidFrom);
    }
}
