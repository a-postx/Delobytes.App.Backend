using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Catalog.Application.Commands.ProductWorkRates.UpdateProductWorkRate;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Application.Services.CostCalculation;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Contracts.Errors;
using FluentAssertions;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Catalog.ProductWorkRates;

/// <summary>
/// Unit tests for UpdateProductWorkRateCommandHandler, run against mocked repositories.
///
/// The RowVersion concurrency token added to ProductWorkRate in this task is enforced by EF
/// Core's SaveChangesAsync against a real database (DbUpdateConcurrencyException -> AppException
/// with ErrorCodes.Common.Conflict, via SaveChangesWithConflictTranslationAsync). A mocked
/// IProductWorkRateRepository never calls SaveChangesAsync on a real DbContext, so no unit test
/// here can exercise that path; it would only assert that the mock does what it was told to do.
/// No PostgreSQL-backed repository test fixture exists in this suite (the other Catalog
/// repository tests all run against the EF InMemory provider, which does not support
/// xmin-based concurrency), so the end-to-end check is left as a gap — see the Stage 1 report.
/// </summary>
public class UpdateProductWorkRateCommandHandlerTests
{
    private readonly Mock<IProductWorkRateRepository> _repositoryMock = new();
    private readonly Mock<IWorkRateRepository> _workRateRepositoryMock = new();
    private readonly Mock<IProductCostSnapshotService> _snapshotServiceMock = new();

    private UpdateProductWorkRateCommandHandler BuildHandler()
    {
        _repositoryMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _repositoryMock
            .Setup(r => r.GetByProductIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ProductWorkRate>());

        return new UpdateProductWorkRateCommandHandler(
            _repositoryMock.Object,
            _workRateRepositoryMock.Object,
            _snapshotServiceMock.Object);
    }

    private static WorkRate BuildWorkRate(Guid id)
        => new WorkRate
        {
            Id = id,
            Name = "Базовая ставка",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private static ProductWorkRate BuildRate(
        Guid productId,
        Guid workRateId,
        DateOnly validFrom,
        bool isActive = true)
        => new ProductWorkRate
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            WorkRateId = workRateId,
            AssemblyRatePerDay = 10,
            ValidFrom = validFrom,
            IsActive = isActive,
            CreatedAt = DateTimeOffset.UtcNow.AddMonths(-1),
        };

    [Fact]
    public async Task Handle_ValidCorrection_UpdatesFieldsAndSetsUpdatedAt()
    {
        Guid productId = Guid.NewGuid();
        Guid workRateId = Guid.NewGuid();

        ProductWorkRate rate = BuildRate(productId, workRateId, new DateOnly(2026, 3, 1));

        _repositoryMock
            .Setup(r => r.GetByIdAsync(rate.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rate);

        UpdateProductWorkRateCommandHandler handler = BuildHandler();

        UpdateProductWorkRateCommand command = new UpdateProductWorkRateCommand
        {
            Id = rate.Id,
            WorkRateId = workRateId,
            AssemblyRatePerDay = 25,
            ValidFrom = new DateOnly(2026, 3, 15),
        };

        UpdateProductWorkRateResponse response = await handler.Handle(command, CancellationToken.None);

        response.Found.Should().BeTrue();
        rate.AssemblyRatePerDay.Should().Be(25);
        rate.ValidFrom.Should().Be(new DateOnly(2026, 3, 15));
        rate.WorkRateId.Should().Be(workRateId);
        rate.UpdatedAt.Should().NotBeNull();

        _repositoryMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ValidCorrection_CapturesSnapshotBeforeMutatingEntity()
    {
        Guid productId = Guid.NewGuid();
        Guid workRateId = Guid.NewGuid();

        ProductWorkRate rate = BuildRate(productId, workRateId, new DateOnly(2026, 3, 1));
        int originalAssemblyRate = rate.AssemblyRatePerDay;
        DateOnly originalValidFrom = rate.ValidFrom;
        bool capturedBeforeMutation = false;

        _repositoryMock
            .Setup(r => r.GetByIdAsync(rate.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rate);

        UpdateProductWorkRateCommandHandler handler = BuildHandler();

        _snapshotServiceMock
            .Setup(s => s.CaptureBeforeChangeAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyCollection<Guid>, string, CancellationToken>(
                (_, _, _) =>
                {
                    // The old figures must still be in effect when the snapshot is calculated.
                    rate.AssemblyRatePerDay.Should().Be(originalAssemblyRate);
                    rate.ValidFrom.Should().Be(originalValidFrom);
                    capturedBeforeMutation = true;
                })
            .Returns(Task.CompletedTask);

        UpdateProductWorkRateCommand command = new UpdateProductWorkRateCommand
        {
            Id = rate.Id,
            WorkRateId = workRateId,
            AssemblyRatePerDay = 30,
            ValidFrom = new DateOnly(2026, 3, 20),
        };

        await handler.Handle(command, CancellationToken.None);

        capturedBeforeMutation.Should().BeTrue();
        rate.AssemblyRatePerDay.Should().Be(30);

        _snapshotServiceMock.Verify(
            s => s.CaptureBeforeChangeAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(productId)),
                "WorkRateChanged",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_UnknownId_ReturnsNotFoundAndCapturesNoSnapshot()
    {
        _repositoryMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProductWorkRate?)null);

        UpdateProductWorkRateCommandHandler handler = BuildHandler();

        UpdateProductWorkRateCommand command = new UpdateProductWorkRateCommand
        {
            Id = Guid.NewGuid(),
            WorkRateId = Guid.NewGuid(),
            AssemblyRatePerDay = 10,
            ValidFrom = new DateOnly(2026, 1, 1),
        };

        UpdateProductWorkRateResponse response = await handler.Handle(command, CancellationToken.None);

        response.Found.Should().BeFalse();

        _snapshotServiceMock.Verify(
            s => s.CaptureBeforeChangeAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        _repositoryMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_SupersededVersion_IsRefused()
    {
        Guid productId = Guid.NewGuid();
        Guid workRateId = Guid.NewGuid();

        ProductWorkRate rate = BuildRate(productId, workRateId, new DateOnly(2026, 1, 1), isActive: false);

        _repositoryMock
            .Setup(r => r.GetByIdAsync(rate.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rate);

        UpdateProductWorkRateCommandHandler handler = BuildHandler();

        UpdateProductWorkRateCommand command = new UpdateProductWorkRateCommand
        {
            Id = rate.Id,
            WorkRateId = workRateId,
            AssemblyRatePerDay = 15,
            ValidFrom = new DateOnly(2026, 2, 1),
        };

        Func<Task> action = () => handler.Handle(command, CancellationToken.None);

        AppException exception = (await action.Should().ThrowAsync<AppException>()).Which;
        exception.Code.Should().Be(ErrorCodes.Catalog.ProductWorkRateNotFound);

        _repositoryMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ValidFromNotStrictlyLaterThanAnotherVersion_IsRefused()
    {
        Guid productId = Guid.NewGuid();
        Guid workRateId = Guid.NewGuid();

        ProductWorkRate rate = BuildRate(productId, workRateId, new DateOnly(2026, 3, 1));
        ProductWorkRate laterVersion = BuildRate(productId, workRateId, new DateOnly(2026, 6, 1));

        _repositoryMock
            .Setup(r => r.GetByIdAsync(rate.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rate);

        UpdateProductWorkRateCommandHandler handler = BuildHandler();

        _repositoryMock
            .Setup(r => r.GetByProductIdAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { rate, laterVersion });

        UpdateProductWorkRateCommand command = new UpdateProductWorkRateCommand
        {
            Id = rate.Id,
            WorkRateId = workRateId,
            AssemblyRatePerDay = 12,
            ValidFrom = new DateOnly(2026, 4, 1), // earlier than laterVersion.ValidFrom
        };

        Func<Task> action = () => handler.Handle(command, CancellationToken.None);

        AppException exception = (await action.Should().ThrowAsync<AppException>()).Which;
        exception.Code.Should().Be(ErrorCodes.Catalog.ProductWorkRateValidFromNotLatest);

        _snapshotServiceMock.Verify(
            s => s.CaptureBeforeChangeAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        _repositoryMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_DuplicateValidFromAgainstAnotherVersion_IsRefused()
    {
        Guid productId = Guid.NewGuid();
        Guid workRateId = Guid.NewGuid();
        DateOnly sharedDate = new DateOnly(2026, 5, 1);

        ProductWorkRate rate = BuildRate(productId, workRateId, new DateOnly(2026, 1, 1));
        ProductWorkRate otherVersion = BuildRate(productId, workRateId, sharedDate);

        _repositoryMock
            .Setup(r => r.GetByIdAsync(rate.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rate);

        UpdateProductWorkRateCommandHandler handler = BuildHandler();

        _repositoryMock
            .Setup(r => r.GetByProductIdAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { rate, otherVersion });

        UpdateProductWorkRateCommand command = new UpdateProductWorkRateCommand
        {
            Id = rate.Id,
            WorkRateId = workRateId,
            AssemblyRatePerDay = 12,
            ValidFrom = sharedDate,
        };

        Func<Task> action = () => handler.Handle(command, CancellationToken.None);

        AppException exception = (await action.Should().ThrowAsync<AppException>()).Which;
        exception.Code.Should().Be(ErrorCodes.Catalog.ProductWorkRateValidFromConflict);

        _repositoryMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_UnknownWorkRateId_IsRefused()
    {
        Guid productId = Guid.NewGuid();
        Guid oldWorkRateId = Guid.NewGuid();
        Guid newWorkRateId = Guid.NewGuid();

        ProductWorkRate rate = BuildRate(productId, oldWorkRateId, new DateOnly(2026, 1, 1));

        _repositoryMock
            .Setup(r => r.GetByIdAsync(rate.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rate);

        _workRateRepositoryMock
            .Setup(r => r.GetByIdAsync(newWorkRateId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkRate?)null);

        UpdateProductWorkRateCommandHandler handler = BuildHandler();

        UpdateProductWorkRateCommand command = new UpdateProductWorkRateCommand
        {
            Id = rate.Id,
            WorkRateId = newWorkRateId,
            AssemblyRatePerDay = 12,
            ValidFrom = new DateOnly(2026, 2, 1),
        };

        Func<Task> action = () => handler.Handle(command, CancellationToken.None);

        AppException exception = (await action.Should().ThrowAsync<AppException>()).Which;
        exception.Code.Should().Be(ErrorCodes.Catalog.WorkRateNotFound);

        _repositoryMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
