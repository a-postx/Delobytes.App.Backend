using System;
using System.Collections.Generic;
using System.Linq;
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
    private readonly Mock<IWorkRateVersionRepository> _versionRepoMock = new();

    private CreateWorkRateCommandHandler CreateWorkRateHandler()
        => new CreateWorkRateCommandHandler(_repoMock.Object, _versionRepoMock.Object);

    private GetWorkRateQueryHandler GetWorkRateHandler()
        => new GetWorkRateQueryHandler(_repoMock.Object, _versionRepoMock.Object);

    private GetWorkRatesQueryHandler GetWorkRatesHandler()
        => new GetWorkRatesQueryHandler(_repoMock.Object, _versionRepoMock.Object);

    private static WorkRate BuildWorkRate(Guid? id = null, string name = "Базовая ставка")
        => new WorkRate
        {
            Id = id ?? Guid.NewGuid(),
            Name = name,
            IsActive = true,
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

    /// <summary>
    /// Stubs the batch active-version lookup used by the list query. An empty map is a valid
    /// answer: a work rate may exist without any wage version.
    /// </summary>
    private void SetupActiveVersions(IReadOnlyDictionary<Guid, WorkRateVersion> versionsByWorkRateId)
    {
        _versionRepoMock
            .Setup(r => r.GetActiveByWorkRateIdsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(versionsByWorkRateId);
    }

    // ── Create ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateWorkRate_ValidCommand_AddsEntityAndReturnsId()
    {
        // Arrange
        _repoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        CreateWorkRateCommandHandler handler = CreateWorkRateHandler();

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
                wr.IsActive == true)),
            Times.Once);

        _repoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateWorkRate_ValidCommand_AddsFirstWageVersionLinkedToTheWorkRate()
    {
        // Arrange
        _repoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        CreateWorkRateCommandHandler handler = CreateWorkRateHandler();

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
        _versionRepoMock.Verify(
            r => r.Add(It.Is<WorkRateVersion>(version =>
                version.WorkRateId == response.Id &&
                version.DailyWage == 3500m &&
                version.ValidFrom == new DateOnly(2026, 4, 1) &&
                version.IsActive == true)),
            Times.Once);

        // The work rate and its first version share the scoped DbContext, so exactly one save
        // must persist both: a second save would mean the two writes are not atomic.
        _repoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _versionRepoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Delete ───────────────────────────────────────────────────────────────────

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

    // ── Update (name) ────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateWorkRate_NewName_PersistsRenamedWorkRate()
    {
        // Arrange
        WorkRate existing = BuildWorkRate();

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
            Name = "Сборщик, 3 разряд",
            IsActive = true,
        };

        // Act
        UpdateWorkRateResponse response =
            await handler.Handle(command, CancellationToken.None);

        // Assert
        response.Found.Should().BeTrue();
        existing.Name.Should().Be("Сборщик, 3 разряд");
        existing.UpdatedAt.Should().NotBeNull();

        _repoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Queries ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetWorkRate_ExistingId_ReturnsMappedResponseWithActiveVersion()
    {
        // Arrange
        WorkRate rate = BuildWorkRate();
        WorkRateVersion version = BuildVersion(rate.Id, dailyWage: 3000m);

        _repoMock
            .Setup(r => r.GetByIdAsync(rate.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rate);

        _versionRepoMock
            .Setup(r => r.GetActiveByWorkRateIdAsync(rate.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(version);

        GetWorkRateQueryHandler handler = GetWorkRateHandler();

        // Act
        GetWorkRateResponse? response = await handler.Handle(
            new GetWorkRateQuery { Id = rate.Id }, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response!.Id.Should().Be(rate.Id);
        response.Name.Should().Be(rate.Name);
        response.IsActive.Should().BeTrue();

        // The legacy flat fields stay populated from the active version so that a client which
        // has not moved to ActiveVersion yet keeps showing the right wage.
        response.DailyWage.Should().Be(3000m);
        response.ValidFrom.Should().Be(new DateOnly(2026, 1, 1));

        response.ActiveVersion.Should().NotBeNull();
        response.ActiveVersion!.Id.Should().Be(version.Id);
        response.ActiveVersion.DailyWage.Should().Be(3000m);
        response.ActiveVersion.ValidFrom.Should().Be("2026-01-01");
    }

    [Fact]
    public async Task GetWorkRate_WithoutActiveVersion_ReturnsNullActiveVersionAndZeroWage()
    {
        // Arrange
        WorkRate rate = BuildWorkRate();

        _repoMock
            .Setup(r => r.GetByIdAsync(rate.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rate);

        _versionRepoMock
            .Setup(r => r.GetActiveByWorkRateIdAsync(rate.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkRateVersion?)null);

        GetWorkRateQueryHandler handler = GetWorkRateHandler();

        // Act
        GetWorkRateResponse? response = await handler.Handle(
            new GetWorkRateQuery { Id = rate.Id }, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response!.ActiveVersion.Should().BeNull();
        response.DailyWage.Should().Be(0m);
        response.ValidFrom.Should().Be(new DateOnly());
    }

    [Fact]
    public async Task GetWorkRate_MissingId_ReturnsNull()
    {
        // Arrange
        _repoMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkRate?)null);

        GetWorkRateQueryHandler handler = GetWorkRateHandler();

        // Act
        GetWorkRateResponse? response = await handler.Handle(
            new GetWorkRateQuery { Id = Guid.NewGuid() }, CancellationToken.None);

        // Assert
        response.Should().BeNull();
        _versionRepoMock.Verify(
            r => r.GetActiveByWorkRateIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetWorkRates_ExistingRates_ResolveActiveVersionsInOneRoundTrip()
    {
        // Arrange
        List<WorkRate> rates = new List<WorkRate>
        {
            BuildWorkRate(name: "Сборщик"),
            BuildWorkRate(name: "Упаковщик"),
        };

        _repoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(rates);

        WorkRateVersion firstVersion = BuildVersion(rates[0].Id, dailyWage: 3000m);
        WorkRateVersion secondVersion = BuildVersion(rates[1].Id, dailyWage: 2500m);

        SetupActiveVersions(new Dictionary<Guid, WorkRateVersion>
        {
            { rates[0].Id, firstVersion },
            { rates[1].Id, secondVersion },
        });

        GetWorkRatesQueryHandler handler = GetWorkRatesHandler();

        // Act
        GetWorkRatesResponse response =
            await handler.Handle(new GetWorkRatesQuery(), CancellationToken.None);

        // Assert
        response.Items.Should().HaveCount(2);
        response.Items.Should().OnlyContain(i => i.DailyWage > 0m);
        response.Items.Should().OnlyContain(i => i.ActiveVersion != null);

        // One batch call for the page, never one call per row.
        _versionRepoMock.Verify(
            r => r.GetActiveByWorkRateIdsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetWorkRates_RateWithoutActiveVersion_ReturnsZeroWage()
    {
        // Arrange
        List<WorkRate> rates = new List<WorkRate> { BuildWorkRate() };

        _repoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(rates);

        SetupActiveVersions(new Dictionary<Guid, WorkRateVersion>());

        GetWorkRatesQueryHandler handler = GetWorkRatesHandler();

        // Act
        GetWorkRatesResponse response =
            await handler.Handle(new GetWorkRatesQuery(), CancellationToken.None);

        // Assert
        response.Items.Should().ContainSingle();
        response.Items[0].DailyWage.Should().Be(0m);
        response.Items[0].ActiveVersion.Should().BeNull();
    }

    [Fact]
    public async Task GetWorkRates_NoRates_DoesNotQueryVersions()
    {
        // Arrange
        _repoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<WorkRate>());

        SetupActiveVersions(new Dictionary<Guid, WorkRateVersion>());

        GetWorkRatesQueryHandler handler = GetWorkRatesHandler();

        // Act
        GetWorkRatesResponse response =
            await handler.Handle(new GetWorkRatesQuery(), CancellationToken.None);

        // Assert
        response.Items.Should().BeEmpty();
        _versionRepoMock.Verify(
            r => r.GetActiveByWorkRateIdsAsync(
                It.Is<IReadOnlyList<Guid>>(ids => ids.Count == 0),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
