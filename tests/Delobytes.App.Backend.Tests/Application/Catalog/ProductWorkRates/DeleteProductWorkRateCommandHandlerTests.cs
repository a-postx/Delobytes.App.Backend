using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Catalog.Application.Commands.ProductWorkRates;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Application.Services.CostCalculation;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using FluentAssertions;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Catalog.ProductWorkRates;

/// <summary>
/// Unit tests for DeleteProductWorkRateCommandHandler.
///
/// Removal is a dated withdrawal, not a flag flip: the version keeps resolving for every date
/// before RemovedFrom, which is the only way a past cost calculation stays reproducible while
/// future ones stop charging labour. The date arithmetic is therefore the substance of these
/// tests, together with the idempotency guard that stops a double submit from moving the date
/// forward.
/// </summary>
public class DeleteProductWorkRateCommandHandlerTests
{
    private readonly Mock<IProductWorkRateRepository> _repositoryMock = new();
    private readonly Mock<IProductCostSnapshotService> _snapshotServiceMock = new();

    private DeleteProductWorkRateCommandHandler BuildHandler()
    {
        _repositoryMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        return new DeleteProductWorkRateCommandHandler(
            _repositoryMock.Object,
            _snapshotServiceMock.Object);
    }

    private static ProductWorkRate BuildRate(
        DateOnly validFrom,
        ProductWorkRateStatus status = ProductWorkRateStatus.Active,
        DateOnly? removedFrom = null)
        => new ProductWorkRate
        {
            Id = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            WorkRateId = Guid.NewGuid(),
            AssemblyRatePerDay = 10,
            ValidFrom = validFrom,
            Status = status,
            RemovedFrom = removedFrom,
            CreatedAt = DateTimeOffset.UtcNow.AddMonths(-1),
        };

    private void ArrangeExistingRate(ProductWorkRate rate)
    {
        _repositoryMock
            .Setup(r => r.GetByIdAsync(rate.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rate);
    }

    [Fact]
    public async Task Handle_VersionStartingInThePast_RemovesItFromToday()
    {
        DateOnly today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
        ProductWorkRate rate = BuildRate(today.AddDays(-30));

        ArrangeExistingRate(rate);

        DeleteProductWorkRateCommandHandler handler = BuildHandler();

        DeleteProductWorkRateResponse response = await handler.Handle(
            new DeleteProductWorkRateCommand { Id = rate.Id },
            CancellationToken.None);

        response.Found.Should().BeTrue();
        rate.Status.Should().Be(ProductWorkRateStatus.Removed);
        rate.RemovedFrom.Should().Be(today);
        rate.UpdatedAt.Should().NotBeNull();

        _repositoryMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_VersionStartingToday_RemovesItFromTomorrow()
    {
        // The version is in force for today, so the earliest date on which it can stop being in
        // force is the day after it started. Setting RemovedFrom to today would leave a version
        // that was never in force for a single day.
        DateOnly today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
        ProductWorkRate rate = BuildRate(today);

        ArrangeExistingRate(rate);

        DeleteProductWorkRateCommandHandler handler = BuildHandler();

        await handler.Handle(new DeleteProductWorkRateCommand { Id = rate.Id }, CancellationToken.None);

        rate.Status.Should().Be(ProductWorkRateStatus.Removed);
        rate.RemovedFrom.Should().Be(today.AddDays(1));
    }

    [Fact]
    public async Task Handle_FutureDatedVersion_RemovesItFromTheDayAfterItsStart()
    {
        DateOnly today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
        DateOnly validFrom = today.AddDays(45);
        ProductWorkRate rate = BuildRate(validFrom);

        ArrangeExistingRate(rate);

        DeleteProductWorkRateCommandHandler handler = BuildHandler();

        await handler.Handle(new DeleteProductWorkRateCommand { Id = rate.Id }, CancellationToken.None);

        rate.Status.Should().Be(ProductWorkRateStatus.Removed);
        rate.RemovedFrom.Should().Be(validFrom.AddDays(1));
    }

    [Fact]
    public async Task Handle_ExistingRate_CapturesSnapshotBeforeMutatingTheRow()
    {
        ProductWorkRate rate = BuildRate(DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-10));
        bool capturedBeforeMutation = false;

        ArrangeExistingRate(rate);

        _snapshotServiceMock
            .Setup(s => s.CaptureBeforeChangeAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyCollection<Guid>, string, CancellationToken>((_, _, _) =>
            {
                // The snapshot describes the product as it was before the removal, so the row must
                // still read as Active when the capture runs.
                rate.Status.Should().Be(ProductWorkRateStatus.Active);
                rate.RemovedFrom.Should().BeNull();
                capturedBeforeMutation = true;
            })
            .Returns(Task.CompletedTask);

        DeleteProductWorkRateCommandHandler handler = BuildHandler();

        await handler.Handle(new DeleteProductWorkRateCommand { Id = rate.Id }, CancellationToken.None);

        capturedBeforeMutation.Should().BeTrue();

        _snapshotServiceMock.Verify(
            s => s.CaptureBeforeChangeAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(rate.ProductId)),
                "WorkRateRemoved",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_AlreadyRemovedVersion_IsIdempotentAndKeepsTheOriginalRemovalDate()
    {
        DateOnly originalRemoval = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-5);
        ProductWorkRate rate = BuildRate(originalRemoval.AddDays(-20), ProductWorkRateStatus.Removed, originalRemoval);

        ArrangeExistingRate(rate);

        DeleteProductWorkRateCommandHandler handler = BuildHandler();

        DeleteProductWorkRateResponse response = await handler.Handle(
            new DeleteProductWorkRateCommand { Id = rate.Id },
            CancellationToken.None);

        // A response, not an exception: the end state the caller asked for is already in place, and
        // moving RemovedFrom forward would extend the period the version is still charged for.
        response.Found.Should().BeTrue();
        rate.RemovedFrom.Should().Be(originalRemoval);

        _snapshotServiceMock.Verify(
            s => s.CaptureBeforeChangeAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        _repositoryMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_UnknownId_ReturnsNotFoundAndCapturesNoSnapshot()
    {
        _repositoryMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProductWorkRate?)null);

        DeleteProductWorkRateCommandHandler handler = BuildHandler();

        DeleteProductWorkRateResponse response = await handler.Handle(
            new DeleteProductWorkRateCommand { Id = Guid.NewGuid() },
            CancellationToken.None);

        response.Found.Should().BeFalse();

        _snapshotServiceMock.Verify(
            s => s.CaptureBeforeChangeAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        _repositoryMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
