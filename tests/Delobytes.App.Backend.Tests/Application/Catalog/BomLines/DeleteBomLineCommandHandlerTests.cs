using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Catalog.Application.Commands.BomLines;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Application.Services.CostCalculation;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Contracts.Accounting;
using Delobytes.App.Backend.Contracts.Errors;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Tests.Infrastructure;
using FluentAssertions;
using FluentAssertions.Specialized;
using Moq;

namespace Delobytes.App.Backend.Tests.Application.Catalog.BomLines;

public class DeleteBomLineCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    // Deliberately not the UTC date: the removal date is a tenant-local calendar date.
    private static readonly DateOnly TenantToday = new DateOnly(2026, 11, 9);

    private readonly Mock<IBomLineRepository> _bomLineRepositoryMock;
    private readonly Mock<IProductCostSnapshotService> _snapshotServiceMock;
    private readonly DeleteBomLineCommandHandler _handler;

    public DeleteBomLineCommandHandlerTests()
    {
        _bomLineRepositoryMock = new Mock<IBomLineRepository>();
        _snapshotServiceMock = new Mock<IProductCostSnapshotService>();

        _bomLineRepositoryMock
            .Setup(repository => repository.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _handler = new DeleteBomLineCommandHandler(
            _bomLineRepositoryMock.Object,
            _snapshotServiceMock.Object,
            TenantTimeTestDoubles.TenantContext(TenantId).Object,
            TenantTimeTestDoubles.TenantTimeService(TenantId, TenantToday).Object);
    }

    [Fact]
    public async Task Handle_MissingLine_ThrowsNotFoundError()
    {
        Guid lineId = Guid.NewGuid();

        _bomLineRepositoryMock
            .Setup(repository => repository.GetByIdAsync(
                lineId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((BomLine?)null);

        Func<Task> action = () => _handler.Handle(
            new DeleteBomLineCommand
            {
                Id = lineId,
            },
            CancellationToken.None);

        ExceptionAssertions<AppException> missingLineException =
            await action.Should().ThrowAsync<AppException>();

        missingLineException.Which.Code
            .Should()
            .Be(ErrorCodes.Catalog.BomLineNotFound);

        _bomLineRepositoryMock.Verify(
            repository => repository.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_AlreadyClosedLine_ThrowsNotFoundError()
    {
        Guid lineId = Guid.NewGuid();

        BomLine line = new BomLine
        {
            Id = lineId,
            ValidFrom = new DateOnly(2025, 1, 1),
            ValidTo = TenantToday.AddDays(-3),
        };

        _bomLineRepositoryMock
            .Setup(repository => repository.GetByIdAsync(
                lineId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(line);

        Func<Task> action = () => _handler.Handle(
            new DeleteBomLineCommand
            {
                Id = lineId,
            },
            CancellationToken.None);

        ExceptionAssertions<AppException> closedLineException =
            await action.Should().ThrowAsync<AppException>();

        closedLineException.Which.Code
            .Should()
            .Be(ErrorCodes.Catalog.BomLineNotFound);

        // A removal that has already taken effect must not be moved: doing so would rewrite a period
        // whose cost has already been reported on.
        line.ValidTo.Should().Be(TenantToday.AddDays(-3));

        _snapshotServiceMock.Verify(
            service => service.CaptureBeforeChangeAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_OpenLine_DatesItsEndAndSavesIt()
    {
        Guid lineId = Guid.NewGuid();
        Guid productId = Guid.NewGuid();

        BomLine line = new BomLine
        {
            Id = lineId,
            ProductId = productId,
            ValidFrom = new DateOnly(2025, 1, 1),
        };

        _bomLineRepositoryMock
            .Setup(repository => repository.GetByIdAsync(
                lineId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(line);

        DeleteBomLineResponse response = await _handler.Handle(
            new DeleteBomLineCommand
            {
                Id = lineId,
            },
            CancellationToken.None);

        response.Found.Should().BeTrue();

        // The version keeps applying for every date before the tenant's today, which is what makes a
        // past cost reproducible after the component has been withdrawn.
        line.ValidTo.Should().Be(TenantToday);
        line.IsEffectiveAt(TenantToday.AddDays(-1)).Should().BeTrue();
        line.IsEffectiveAt(TenantToday).Should().BeFalse();
        line.UpdatedAt.Should().NotBeNull();

        _bomLineRepositoryMock.Verify(
            repository => repository.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_OpenLine_CapturesSnapshotBeforeTheRemoval()
    {
        Guid lineId = Guid.NewGuid();
        Guid productId = Guid.NewGuid();

        BomLine line = new BomLine
        {
            Id = lineId,
            ProductId = productId,
            ValidFrom = new DateOnly(2025, 1, 1),
        };

        bool lineWasOpenAtCapture = false;

        _bomLineRepositoryMock
            .Setup(repository => repository.GetByIdAsync(
                lineId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(line);

        _snapshotServiceMock
            .Setup(service => service.CaptureBeforeChangeAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyCollection<Guid>, string, CancellationToken>(
                (_, _, _) => lineWasOpenAtCapture = line.ValidTo == null)
            .Returns(Task.CompletedTask);

        await _handler.Handle(
            new DeleteBomLineCommand
            {
                Id = lineId,
            },
            CancellationToken.None);

        // Removal changes material, logistics and packaging at once, so it is a cost-changing event
        // like any other input edit and has to leave the same trail.
        lineWasOpenAtCapture.Should().BeTrue();

        _snapshotServiceMock.Verify(
            service => service.CaptureBeforeChangeAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(productId)),
                "BomLineRemoved",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_FutureDatedLine_IsClosedWithoutInvertingItsInterval()
    {
        Guid lineId = Guid.NewGuid();

        BomLine line = new BomLine
        {
            Id = lineId,
            ProductId = Guid.NewGuid(),
            ValidFrom = TenantToday.AddDays(10),
        };

        _bomLineRepositoryMock
            .Setup(repository => repository.GetByIdAsync(
                lineId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(line);

        await _handler.Handle(
            new DeleteBomLineCommand
            {
                Id = lineId,
            },
            CancellationToken.None);

        // A version withdrawn before it ever started was never in force; the interval collapses to
        // empty instead of running backwards.
        line.ValidTo.Should().Be(line.ValidFrom);
        line.IsEffectiveAt(line.ValidFrom.AddDays(5)).Should().BeFalse();
    }
}
