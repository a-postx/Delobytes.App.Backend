using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Catalog.Application.Commands.ProductWorkRates.CreateProductWorkRate;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Application.Services.CostCalculation;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Contracts.Errors;
using FluentAssertions;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Catalog.ProductWorkRates;

public class CreateProductWorkRateCommandHandlerTests
{
    private readonly Mock<IProductWorkRateRepository> _repositoryMock = new();
    private readonly Mock<IWorkRateRepository> _workRateRepositoryMock = new();
    private readonly Mock<IProductCostSnapshotService> _snapshotServiceMock = new();

    private CreateProductWorkRateCommandHandler BuildHandler()
    {
        _repositoryMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        return new CreateProductWorkRateCommandHandler(
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

    [Fact]
    public async Task Handle_ValidCommand_AddsVersionedRate()
    {
        Guid productId = Guid.NewGuid();
        Guid workRateId = Guid.NewGuid();

        _workRateRepositoryMock
            .Setup(r => r.GetByIdAsync(workRateId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildWorkRate(workRateId));

        CreateProductWorkRateCommandHandler handler = BuildHandler();

        CreateProductWorkRateCommand command = new CreateProductWorkRateCommand
        {
            ProductId = productId,
            WorkRateId = workRateId,
            AssemblyRatePerDay = 8,
            ValidFrom = new DateOnly(2026, 4, 1),
        };

        CreateProductWorkRateResponse response =
            await handler.Handle(command, CancellationToken.None);

        response.Id.Should().NotBe(Guid.Empty);

        _repositoryMock.Verify(
            r => r.Add(It.Is<ProductWorkRate>(rate =>
                rate.ProductId == productId &&
                rate.WorkRateId == workRateId &&
                rate.AssemblyRatePerDay == 8 &&
                rate.ValidFrom == new DateOnly(2026, 4, 1) &&
                rate.IsActive)),
            Times.Once);

        _repositoryMock.Verify(
            r => r.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── Этап 5: автоматическая фиксация снапшота ─────────────────────────────────────

    [Fact]
    public async Task Handle_ValidCommand_CapturesSnapshotForProductWithWorkRateChangedReason()
    {
        Guid productId = Guid.NewGuid();
        Guid workRateId = Guid.NewGuid();

        _workRateRepositoryMock
            .Setup(r => r.GetByIdAsync(workRateId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildWorkRate(workRateId));

        CreateProductWorkRateCommandHandler handler = BuildHandler();

        CreateProductWorkRateCommand command = new CreateProductWorkRateCommand
        {
            ProductId = productId,
            WorkRateId = workRateId,
            AssemblyRatePerDay = 12,
            ValidFrom = new DateOnly(2026, 5, 1),
        };

        await handler.Handle(command, CancellationToken.None);

        _snapshotServiceMock.Verify(
            s => s.CaptureBeforeChangeAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids =>
                    ids.Count == 1 && ids.Contains(productId)),
                "WorkRateChanged",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_ValidCommand_CapturesSnapshotBeforeNewRateIsSaved()
    {
        Guid productId = Guid.NewGuid();
        Guid workRateId = Guid.NewGuid();

        bool savedAtCapture = false;
        bool addedAtCapture = false;

        _workRateRepositoryMock
            .Setup(r => r.GetByIdAsync(workRateId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildWorkRate(workRateId));

        _repositoryMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => savedAtCapture = true)
            .ReturnsAsync(1);

        _repositoryMock
            .Setup(r => r.Add(It.IsAny<ProductWorkRate>()))
            .Callback<ProductWorkRate>(_ => addedAtCapture = true);

        _snapshotServiceMock
            .Setup(s => s.CaptureBeforeChangeAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyCollection<Guid>, string, CancellationToken>(
                (_, _, _) =>
                {
                    // The rate that is about to replace the current one must not be visible in the
                    // context yet: the raw calculation resolves the effective version by date and
                    // would otherwise pick the freshly added row.
                    savedAtCapture.Should().BeFalse();
                    addedAtCapture.Should().BeFalse();
                })
            .Returns(Task.CompletedTask);

        CreateProductWorkRateCommandHandler handler =
            new CreateProductWorkRateCommandHandler(
                _repositoryMock.Object,
                _workRateRepositoryMock.Object,
                _snapshotServiceMock.Object);

        CreateProductWorkRateCommand command = new CreateProductWorkRateCommand
        {
            ProductId = productId,
            WorkRateId = workRateId,
            AssemblyRatePerDay = 16,
            ValidFrom = DateOnly.FromDateTime(DateTime.UtcNow),
        };

        await handler.Handle(command, CancellationToken.None);

        _snapshotServiceMock.Verify(
            s => s.CaptureBeforeChangeAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_MissingWorkRate_DoesNotCaptureSnapshot()
    {
        Guid workRateId = Guid.NewGuid();

        _workRateRepositoryMock
            .Setup(r => r.GetByIdAsync(workRateId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkRate?)null);

        CreateProductWorkRateCommandHandler handler = BuildHandler();

        CreateProductWorkRateCommand command = new CreateProductWorkRateCommand
        {
            ProductId = Guid.NewGuid(),
            WorkRateId = workRateId,
            AssemblyRatePerDay = 8,
            ValidFrom = new DateOnly(2026, 1, 1),
        };

        Func<Task> action = () => handler.Handle(command, CancellationToken.None);

        await action.Should().ThrowAsync<AppException>();

        _snapshotServiceMock.Verify(
            s => s.CaptureBeforeChangeAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        _repositoryMock.Verify(
            r => r.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
