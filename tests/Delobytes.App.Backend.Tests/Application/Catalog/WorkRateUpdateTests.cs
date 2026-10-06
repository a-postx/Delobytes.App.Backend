using System;
using System.Collections.Generic;
using System.Linq;
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

    private static WorkRate BuildWorkRate(Guid? id = null, bool isActive = true, string name = "Базовая ставка")
        => new WorkRate
        {
            Id = id ?? Guid.NewGuid(),
            Name = name,
            IsActive = isActive,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private static WorkRateVersion BuildVersion(
        Guid workRateId,
        decimal dailyWage = 3000m,
        DateOnly? validFrom = null,
        bool isActive = true)
        => new WorkRateVersion
        {
            Id = Guid.NewGuid(),
            WorkRateId = workRateId,
            DailyWage = dailyWage,
            ValidFrom = validFrom ?? new DateOnly(2026, 1, 1),
            IsActive = isActive,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private UpdateWorkRateCommandHandler BuildHandler()
        => new UpdateWorkRateCommandHandler(_repoMock.Object);

    // ── Update (IsActive Toggle) ─────────────────────────────────────────────────

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

        UpdateWorkRateCommandHandler handler = BuildHandler();

        UpdateWorkRateCommand command = new UpdateWorkRateCommand
        {
            Id = existing.Id,
            Name = existing.Name,
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

        UpdateWorkRateCommandHandler handler = BuildHandler();

        UpdateWorkRateCommand command = new UpdateWorkRateCommand
        {
            Id = existing.Id,
            Name = existing.Name,
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

        UpdateWorkRateCommandHandler handler = BuildHandler();

        UpdateWorkRateCommand command = new UpdateWorkRateCommand
        {
            Id = Guid.NewGuid(),
            Name = "Ставка без записи",
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

        UpdateWorkRateCommandHandler handler = BuildHandler();

        UpdateWorkRateCommand command = new UpdateWorkRateCommand
        {
            Id = existing.Id,
            Name = existing.Name,
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

    // ── Update (Name) ────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateWorkRate_NewName_RenamesWorkRate()
    {
        // Arrange
        WorkRate existing = BuildWorkRate(name: "Базовая ставка");

        _repoMock
            .Setup(r => r.GetByIdAsync(existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        _repoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        UpdateWorkRateCommandHandler handler = BuildHandler();

        UpdateWorkRateCommand command = new UpdateWorkRateCommand
        {
            Id = existing.Id,
            Name = "Сборщик, 3 разряд",
            IsActive = true,
        };

        DateTimeOffset beforeCall = DateTimeOffset.UtcNow;

        // Act
        UpdateWorkRateResponse response =
            await handler.Handle(command, CancellationToken.None);

        // Assert
        response.Found.Should().BeTrue();
        existing.Name.Should().Be("Сборщик, 3 разряд");
        existing.IsActive.Should().BeTrue();
        existing.UpdatedAt.Should().BeOnOrAfter(beforeCall);

        _repoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateWorkRate_RenameAndDeactivate_AppliesBothFields()
    {
        // Arrange
        WorkRate existing = BuildWorkRate(isActive: true, name: "Базовая ставка");

        _repoMock
            .Setup(r => r.GetByIdAsync(existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        _repoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        UpdateWorkRateCommandHandler handler = BuildHandler();

        UpdateWorkRateCommand command = new UpdateWorkRateCommand
        {
            Id = existing.Id,
            Name = "Упаковщик",
            IsActive = false,
        };

        // Act
        UpdateWorkRateResponse response =
            await handler.Handle(command, CancellationToken.None);

        // Assert
        response.Found.Should().BeTrue();
        existing.Name.Should().Be("Упаковщик");
        existing.IsActive.Should().BeFalse();
    }

    // ── Update does not touch wages ──────────────────────────────────────────────

    [Fact]
    public async Task UpdateWorkRate_DoesNotTouchWageVersions()
    {
        // The wage is versioned through CreateWorkRateVersionCommand; this command must not
        // reach for the version repository at all. The whole point of the split is that editing
        // the name cannot move a historical cost, so the handler's dependency list is asserted
        // rather than assumed.
        WorkRate existing = BuildWorkRate();

        _repoMock
            .Setup(r => r.GetByIdAsync(existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        _repoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        WorkRateVersion activeVersion = BuildVersion(existing.Id, dailyWage: 3000m);
        existing.Versions.Add(activeVersion);

        UpdateWorkRateCommandHandler handler = BuildHandler();

        UpdateWorkRateCommand command = new UpdateWorkRateCommand
        {
            Id = existing.Id,
            Name = "Переименованная ставка",
            IsActive = true,
        };

        // Act
        await handler.Handle(command, CancellationToken.None);

        // Assert
        existing.Name.Should().Be("Переименованная ставка");
        existing.Versions.Should().ContainSingle();
        existing.Versions.Single().DailyWage.Should().Be(3000m);
        existing.Versions.Single().ValidFrom.Should().Be(new DateOnly(2026, 1, 1));
        existing.Versions.Single().IsActive.Should().BeTrue();
    }

    // ── Этап 5: отсутствие триггера снапшота ─────────────────────────────────────

    [Fact]
    public async Task UpdateWorkRate_DoesNotTakeSnapshotDependency()
    {
        // UpdateWorkRate can only rename and toggle IsActive, and neither the name nor the
        // activity flag takes part in the cost calculation: the wage is resolved from
        // WorkRateVersions, whose lookup ignores IsActive. This command therefore cannot move a
        // product cost and takes no IProductCostSnapshotService — the constructor stays
        // single-argument. The test pins that down, because giving this handler the snapshot
        // service would mean capturing on a change that does not affect the calculation.
        Type[] parameterTypes = typeof(UpdateWorkRateCommandHandler)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        parameterTypes.Should().Equal(typeof(IWorkRateRepository));
    }
}
