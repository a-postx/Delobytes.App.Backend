using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Catalog.Application.Commands.WorkRates.CreateWorkRateVersion;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Application.Services.CostCalculation;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using FluentAssertions;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Catalog;

public class CreateWorkRateVersionCommandHandlerTests
{
    private readonly Mock<IWorkRateRepository> _workRateRepoMock = new();
    private readonly Mock<IWorkRateVersionRepository> _versionRepoMock = new();
    private readonly Mock<IProductWorkRateRepository> _productWorkRateRepoMock = new();
    private readonly Mock<IProductCostSnapshotService> _snapshotServiceMock = new();

    private CreateWorkRateVersionCommandHandler BuildHandler(
        IReadOnlyCollection<Guid>? affectedProductIds = null)
    {
        // The stub is configured here and only here: a separate Setup inside a test body would be
        // silently overridden, because Moq honours the last matching configuration.
        _productWorkRateRepoMock
            .Setup(r => r.GetProductIdsByWorkRateIdAsync(
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(affectedProductIds != null ? affectedProductIds.ToList() : Array.Empty<Guid>());

        return new CreateWorkRateVersionCommandHandler(
            _workRateRepoMock.Object,
            _versionRepoMock.Object,
            _productWorkRateRepoMock.Object,
            _snapshotServiceMock.Object);
    }

    private static WorkRate BuildWorkRate(Guid? id = null)
    {
        WorkRate workRate = new WorkRate
        {
            Id = id ?? Guid.NewGuid(),
            Name = "Базовая ставка",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        return workRate;
    }

    private static WorkRateVersion BuildVersion(
        Guid workRateId,
        decimal dailyWage = 3000m,
        DateOnly? validFrom = null,
        bool isActive = true)
    {
        WorkRateVersion version = new WorkRateVersion
        {
            Id = Guid.NewGuid(),
            WorkRateId = workRateId,
            DailyWage = dailyWage,
            ValidFrom = validFrom ?? new DateOnly(2026, 1, 1),
            IsActive = isActive,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        return version;
    }

    // ── CreateWorkRateVersion ────────────────────────────────────────────────────

    [Fact]
    public async Task CreateWorkRateVersion_ValidCommand_CreatesNewActiveVersion()
    {
        // Arrange
        WorkRate workRate = BuildWorkRate();

        _workRateRepoMock
            .Setup(r => r.GetByIdAsync(workRate.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workRate);

        _versionRepoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        CreateWorkRateVersionCommandHandler handler = BuildHandler();

        CreateWorkRateVersionCommand command = new CreateWorkRateVersionCommand
        {
            WorkRateId = workRate.Id,
            DailyWage = 3500m,
            ValidFrom = new DateOnly(2026, 4, 1),
        };

        // Act
        CreateWorkRateVersionResponse response =
            await handler.Handle(command, CancellationToken.None);

        // Assert
        response.Id.Should().NotBe(Guid.Empty);
        response.Found.Should().BeTrue();

        _versionRepoMock.Verify(
            r => r.Add(It.Is<WorkRateVersion>(version =>
                version.WorkRateId == workRate.Id &&
                version.DailyWage == 3500m &&
                version.ValidFrom == new DateOnly(2026, 4, 1) &&
                version.IsActive == true)),
            Times.Once);

        _versionRepoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateWorkRateVersion_MissingWorkRate_ReturnsFoundFalseWithoutSaving()
    {
        // Arrange
        _workRateRepoMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkRate?)null);

        CreateWorkRateVersionCommandHandler handler = BuildHandler();

        CreateWorkRateVersionCommand command = new CreateWorkRateVersionCommand
        {
            WorkRateId = Guid.NewGuid(),
            DailyWage = 3500m,
            ValidFrom = new DateOnly(2026, 4, 1),
        };

        // Act
        CreateWorkRateVersionResponse response =
            await handler.Handle(command, CancellationToken.None);

        // Assert
        response.Found.Should().BeFalse();
        response.Id.Should().Be(Guid.Empty);

        _versionRepoMock.Verify(r => r.Add(It.IsAny<WorkRateVersion>()), Times.Never);
        _versionRepoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateWorkRateVersion_WithActiveVersion_DeactivatesThePreviousOne()
    {
        // Arrange
        WorkRate workRate = BuildWorkRate();
        WorkRateVersion oldVersion = BuildVersion(workRate.Id, dailyWage: 3000m);
        workRate.Versions.Add(oldVersion);

        _workRateRepoMock
            .Setup(r => r.GetByIdAsync(workRate.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workRate);

        _versionRepoMock
            .Setup(r => r.GetActiveByWorkRateIdAsync(workRate.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(oldVersion);

        _versionRepoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        CreateWorkRateVersionCommandHandler handler = BuildHandler();

        CreateWorkRateVersionCommand command = new CreateWorkRateVersionCommand
        {
            WorkRateId = workRate.Id,
            DailyWage = 3500m,
            ValidFrom = new DateOnly(2026, 4, 1),
        };

        // Act
        CreateWorkRateVersionResponse response =
            await handler.Handle(command, CancellationToken.None);

        // Assert
        response.Found.Should().BeTrue();

        oldVersion.IsActive.Should().BeFalse();
        oldVersion.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateWorkRateVersion_PreservesExistingVersions()
    {
        // The previous wage is superseded, never overwritten: historical cost snapshots reference
        // the figure that was in force on their own date.
        WorkRate workRate = BuildWorkRate();
        WorkRateVersion oldVersion = BuildVersion(
            workRate.Id,
            dailyWage: 3000m,
            validFrom: new DateOnly(2025, 1, 1));
        workRate.Versions.Add(oldVersion);

        _workRateRepoMock
            .Setup(r => r.GetByIdAsync(workRate.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workRate);

        _versionRepoMock
            .Setup(r => r.GetActiveByWorkRateIdAsync(workRate.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(oldVersion);

        _versionRepoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        CreateWorkRateVersionCommandHandler handler = BuildHandler();

        CreateWorkRateVersionCommand command = new CreateWorkRateVersionCommand
        {
            WorkRateId = workRate.Id,
            DailyWage = 4500m,
            ValidFrom = new DateOnly(2026, 7, 1),
        };

        // Act
        await handler.Handle(command, CancellationToken.None);

        // Assert
        workRate.Versions.Should().Contain(oldVersion);
        oldVersion.DailyWage.Should().Be(3000m);
        oldVersion.ValidFrom.Should().Be(new DateOnly(2025, 1, 1));

        _versionRepoMock.Verify(
            r => r.Add(It.Is<WorkRateVersion>(version =>
                version.DailyWage == 4500m &&
                version.ValidFrom == new DateOnly(2026, 7, 1) &&
                version.IsActive == true)),
            Times.Once);
    }

    [Fact]
    public async Task CreateWorkRateVersion_WithoutActiveVersion_AddsVersionWithoutDeactivation()
    {
        // Arrange
        WorkRate workRate = BuildWorkRate();

        _workRateRepoMock
            .Setup(r => r.GetByIdAsync(workRate.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workRate);

        _versionRepoMock
            .Setup(r => r.GetActiveByWorkRateIdAsync(workRate.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkRateVersion?)null);

        _versionRepoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        CreateWorkRateVersionCommandHandler handler = BuildHandler();

        CreateWorkRateVersionCommand command = new CreateWorkRateVersionCommand
        {
            WorkRateId = workRate.Id,
            DailyWage = 3500m,
            ValidFrom = new DateOnly(2026, 4, 1),
        };

        // Act
        CreateWorkRateVersionResponse response =
            await handler.Handle(command, CancellationToken.None);

        // Assert
        response.Found.Should().BeTrue();

        _versionRepoMock.Verify(r => r.Add(It.IsAny<WorkRateVersion>()), Times.Once);
        _versionRepoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateWorkRateVersion_TouchesParentUpdatedAt()
    {
        // Arrange
        WorkRate workRate = BuildWorkRate();
        DateTimeOffset originalUpdatedAt = DateTimeOffset.UtcNow.AddDays(-5);
        workRate.UpdatedAt = originalUpdatedAt;

        _workRateRepoMock
            .Setup(r => r.GetByIdAsync(workRate.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workRate);

        _versionRepoMock
            .Setup(r => r.GetActiveByWorkRateIdAsync(workRate.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkRateVersion?)null);

        _versionRepoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        CreateWorkRateVersionCommandHandler handler = BuildHandler();

        CreateWorkRateVersionCommand command = new CreateWorkRateVersionCommand
        {
            WorkRateId = workRate.Id,
            DailyWage = 3500m,
            ValidFrom = new DateOnly(2026, 4, 1),
        };

        // Act
        await handler.Handle(command, CancellationToken.None);

        // Assert
        workRate.UpdatedAt.Should().NotBeNull();
        workRate.UpdatedAt.Should().BeAfter(originalUpdatedAt);
    }

    [Fact]
    public async Task CreateWorkRateVersion_AffectedProducts_CapturesSnapshotsBeforeTheChange()
    {
        // A wage change moves the cost of every product assembled at this rate, so the figures in
        // force before the change have to be captured first.
        Guid productId = Guid.NewGuid();

        WorkRate workRate = BuildWorkRate();

        _workRateRepoMock
            .Setup(r => r.GetByIdAsync(workRate.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workRate);

        _versionRepoMock
            .Setup(r => r.GetActiveByWorkRateIdAsync(workRate.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkRateVersion?)null);

        _versionRepoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        CreateWorkRateVersionCommandHandler handler = BuildHandler(new[] { productId });

        CreateWorkRateVersionCommand command = new CreateWorkRateVersionCommand
        {
            WorkRateId = workRate.Id,
            DailyWage = 3500m,
            ValidFrom = new DateOnly(2026, 4, 1),
        };

        // Act
        await handler.Handle(command, CancellationToken.None);

        // Assert
        _snapshotServiceMock.Verify(
            s => s.CaptureBeforeChangeAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(productId)),
                "WorkRateChanged",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateWorkRateVersion_NoAffectedProducts_StillCapturesEmptySet()
    {
        // Arrange
        WorkRate workRate = BuildWorkRate();

        _workRateRepoMock
            .Setup(r => r.GetByIdAsync(workRate.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workRate);

        _versionRepoMock
            .Setup(r => r.GetActiveByWorkRateIdAsync(workRate.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkRateVersion?)null);

        _versionRepoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        CreateWorkRateVersionCommandHandler handler = BuildHandler();

        CreateWorkRateVersionCommand command = new CreateWorkRateVersionCommand
        {
            WorkRateId = workRate.Id,
            DailyWage = 3500m,
            ValidFrom = new DateOnly(2026, 4, 1),
        };

        // Act
        await handler.Handle(command, CancellationToken.None);

        // Assert
        _snapshotServiceMock.Verify(
            s => s.CaptureBeforeChangeAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 0),
                "WorkRateChanged",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateWorkRateVersion_ValidFromInThePast_KeepsRequestedDate()
    {
        // Backfilling a wage that applied earlier is a legitimate edit; the handler must not
        // clamp the date to today.
        WorkRate workRate = BuildWorkRate();

        _workRateRepoMock
            .Setup(r => r.GetByIdAsync(workRate.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workRate);

        _versionRepoMock
            .Setup(r => r.GetActiveByWorkRateIdAsync(workRate.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkRateVersion?)null);

        _versionRepoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        CreateWorkRateVersionCommandHandler handler = BuildHandler();

        CreateWorkRateVersionCommand command = new CreateWorkRateVersionCommand
        {
            WorkRateId = workRate.Id,
            DailyWage = 2800m,
            ValidFrom = new DateOnly(2025, 6, 1),
        };

        // Act
        await handler.Handle(command, CancellationToken.None);

        // Assert
        _versionRepoMock.Verify(
            r => r.Add(It.Is<WorkRateVersion>(version => version.ValidFrom == new DateOnly(2025, 6, 1))),
            Times.Once);
    }
}
