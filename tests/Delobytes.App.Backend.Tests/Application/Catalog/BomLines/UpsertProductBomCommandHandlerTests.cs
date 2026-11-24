using System;
using System.Collections.Generic;
using System.Linq;
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

public class UpsertProductBomCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    // Deliberately not the UTC date: BomLine.ValidFrom is a tenant-local calendar date.
    private static readonly DateOnly TenantToday = new DateOnly(2026, 11, 9);

    private readonly Mock<IBomLineRepository> _bomLineRepositoryMock;
    private readonly Mock<IComponentRepository> _componentRepositoryMock;
    private readonly Mock<IProductCostSnapshotService> _snapshotServiceMock;
    private readonly UpsertProductBomCommandHandler _handler;

    public UpsertProductBomCommandHandlerTests()
    {
        _bomLineRepositoryMock = new Mock<IBomLineRepository>();
        _componentRepositoryMock = new Mock<IComponentRepository>();
        _snapshotServiceMock = new Mock<IProductCostSnapshotService>();

        _bomLineRepositoryMock
            .Setup(repository => repository.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _componentRepositoryMock
            .Setup(repository => repository.GetByIdAsync(
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                (Guid id, CancellationToken _) => new Component
                {
                    Id = id,
                    Name = "Component",
                });

        _handler = new UpsertProductBomCommandHandler(
            _bomLineRepositoryMock.Object,
            _componentRepositoryMock.Object,
            _snapshotServiceMock.Object,
            TenantTimeTestDoubles.TenantContext(TenantId).Object,
            TenantTimeTestDoubles.TenantTimeService(TenantId, TenantToday).Object);
    }

    [Fact]
    public async Task Handle_DuplicateComponentIds_ThrowsValidationError()
    {
        Guid duplicateId = Guid.NewGuid();

        UpsertProductBomCommand command = new UpsertProductBomCommand
        {
            ProductId = Guid.NewGuid(),
            Lines = new List<UpsertProductBomItem>
            {
                new UpsertProductBomItem
                {
                    ComponentId = duplicateId,
                    Quantity = 1m,
                },
                new UpsertProductBomItem
                {
                    ComponentId = duplicateId,
                    Quantity = 2m,
                },
            },
        };

        Func<Task> action = () => _handler.Handle(command, CancellationToken.None);

        ExceptionAssertions<AppException> exception =
            await action.Should().ThrowAsync<AppException>();

        exception.Which.Code.Should().Be(ErrorCodes.Common.ValidationFailed);

        _bomLineRepositoryMock.Verify(
            repository => repository.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_InvalidQuantity_ThrowsDomainError()
    {
        UpsertProductBomCommand command = new UpsertProductBomCommand
        {
            ProductId = Guid.NewGuid(),
            Lines = new List<UpsertProductBomItem>
            {
                new UpsertProductBomItem
                {
                    ComponentId = Guid.NewGuid(),
                    Quantity = -1m,
                },
            },
        };

        Func<Task> action = () => _handler.Handle(command, CancellationToken.None);

        ExceptionAssertions<AppException> exception =
            await action.Should().ThrowAsync<AppException>();

        exception.Which.Code.Should().Be(CatalogErrorCodes.BomLineInvalidQuantity);
    }

    [Fact]
    public async Task Handle_ReplacesAllOpenLinesWithNewLines()
    {
        Guid productId = Guid.NewGuid();
        Guid firstComponentId = Guid.NewGuid();
        Guid secondComponentId = Guid.NewGuid();

        BomLine previous = new BomLine
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            ComponentId = Guid.NewGuid(),
            Quantity = 5m,
            ValidFrom = new DateOnly(2025, 1, 1),
        };

        List<BomLine> addedLines = new List<BomLine>();

        _bomLineRepositoryMock
            .Setup(repository => repository.GetOpenByProductIdAsync(
                productId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BomLine> { previous });

        _bomLineRepositoryMock
            .Setup(repository => repository.Add(It.IsAny<BomLine>()))
            .Callback<BomLine>(line => addedLines.Add(line));

        UpsertProductBomCommand command = new UpsertProductBomCommand
        {
            ProductId = productId,
            Lines = new List<UpsertProductBomItem>
            {
                new UpsertProductBomItem
                {
                    ComponentId = firstComponentId,
                    Quantity = 1.5m,
                },
                new UpsertProductBomItem
                {
                    ComponentId = secondComponentId,
                    Quantity = 2m,
                },
            },
        };

        UpsertProductBomResponse response =
            await _handler.Handle(command, CancellationToken.None);

        response.Count.Should().Be(2);

        // The previous version ends on the tenant's calendar day, not on the UTC date, and the new
        // versions start on that same day: adjacent half-open intervals, no overlap and no gap.
        previous.ValidTo.Should().Be(TenantToday);
        previous.UpdatedAt.Should().NotBeNull();

        addedLines.Should().HaveCount(2);
        addedLines.Should().OnlyContain(line =>
            line.ProductId == productId &&
            line.ValidTo == null);

        addedLines
            .Select(line => line.ComponentId)
            .Should()
            .BeEquivalentTo(new[] { firstComponentId, secondComponentId });

        addedLines
            .Select(line => line.Quantity)
            .Should()
            .BeEquivalentTo(new[] { 1.5m, 2m });

        addedLines
            .Select(line => line.ValidFrom)
            .Should()
            .OnlyContain(date => date == TenantToday);

        _bomLineRepositoryMock.Verify(
            repository => repository.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_ComponentDroppedFromComposition_GetsADatedEndNotAFlag()
    {
        Guid productId = Guid.NewGuid();
        Guid keptComponentId = Guid.NewGuid();
        Guid droppedComponentId = Guid.NewGuid();

        BomLine keptLine = new BomLine
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            ComponentId = keptComponentId,
            Quantity = 1m,
            ValidFrom = new DateOnly(2025, 6, 1),
        };

        BomLine droppedLine = new BomLine
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            ComponentId = droppedComponentId,
            Quantity = 5m,
            ValidFrom = new DateOnly(2025, 6, 1),
        };

        _bomLineRepositoryMock
            .Setup(repository => repository.GetOpenByProductIdAsync(
                productId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BomLine> { keptLine, droppedLine });

        UpsertProductBomCommand command = new UpsertProductBomCommand
        {
            ProductId = productId,
            Lines = new List<UpsertProductBomItem>
            {
                new UpsertProductBomItem
                {
                    ComponentId = keptComponentId,
                    Quantity = 2m,
                },
            },
        };

        await _handler.Handle(command, CancellationToken.None);

        // The dropped component must stop counting from today while staying in force for every
        // earlier date. A boolean flag could express the first half only, which is why a cost
        // calculated for a past date used to be unreproducible.
        droppedLine.ValidTo.Should().Be(TenantToday);
        droppedLine.ValidFrom.Should().Be(new DateOnly(2025, 6, 1));
        keptLine.ValidTo.Should().Be(TenantToday);
    }

    [Fact]
    public async Task Handle_VersionStartedToday_IsClosedWithoutInvertingItsInterval()
    {
        Guid productId = Guid.NewGuid();

        BomLine sameDayVersion = new BomLine
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            ComponentId = Guid.NewGuid(),
            Quantity = 1m,
            ValidFrom = TenantToday,
        };

        _bomLineRepositoryMock
            .Setup(repository => repository.GetOpenByProductIdAsync(
                productId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BomLine> { sameDayVersion });

        UpsertProductBomCommand command = new UpsertProductBomCommand
        {
            ProductId = productId,
            Lines = new List<UpsertProductBomItem>
            {
                new UpsertProductBomItem
                {
                    ComponentId = Guid.NewGuid(),
                    Quantity = 1m,
                },
            },
        };

        await _handler.Handle(command, CancellationToken.None);

        // Two edits on one day: the first version ends where it started, so the effective-date query
        // ignores it while the history keeps the row. An earlier ValidTo would be an inverted range.
        sameDayVersion.ValidTo.Should().Be(TenantToday);
        sameDayVersion.IsEffectiveAt(TenantToday).Should().BeFalse();
    }

    // ── Этап 5: автоматическая фиксация снапшота ────────────────────────────────────

    [Fact]
    public async Task Handle_ValidCommand_CapturesSnapshotForProductWithBomChangedReason()
    {
        Guid productId = Guid.NewGuid();

        _bomLineRepositoryMock
            .Setup(repository => repository.GetOpenByProductIdAsync(
                productId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BomLine>());

        UpsertProductBomCommand command = new UpsertProductBomCommand
        {
            ProductId = productId,
            Lines = new List<UpsertProductBomItem>
            {
                new UpsertProductBomItem
                {
                    ComponentId = Guid.NewGuid(),
                    Quantity = 1m,
                },
            },
        };

        await _handler.Handle(command, CancellationToken.None);

        _snapshotServiceMock.Verify(
            service => service.CaptureBeforeChangeAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids =>
                    ids.Count == 1 && ids.Contains(productId)),
                "BomChanged",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_ValidCommand_CapturesSnapshotBeforeOpenLinesAreClosed()
    {
        Guid productId = Guid.NewGuid();

        BomLine previous = new BomLine
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            ComponentId = Guid.NewGuid(),
            Quantity = 5m,
            ValidFrom = new DateOnly(2025, 1, 1),
        };

        bool previousLineWasOpenAtCapture = false;
        int addedLineCount = 0;

        _bomLineRepositoryMock
            .Setup(repository => repository.GetOpenByProductIdAsync(
                productId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BomLine> { previous });

        _bomLineRepositoryMock
            .Setup(repository => repository.Add(It.IsAny<BomLine>()))
            .Callback<BomLine>(_ => addedLineCount++);

        _snapshotServiceMock
            .Setup(service => service.CaptureBeforeChangeAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyCollection<Guid>, string, CancellationToken>(
                (_, _, _) =>
                {
                    // The full replacement must not have started yet, otherwise the snapshot
                    // would capture the new composition instead of the one being replaced.
                    previousLineWasOpenAtCapture = previous.ValidTo == null;
                    addedLineCount.Should().Be(0);
                })
            .Returns(Task.CompletedTask);

        UpsertProductBomCommand command = new UpsertProductBomCommand
        {
            ProductId = productId,
            Lines = new List<UpsertProductBomItem>
            {
                new UpsertProductBomItem
                {
                    ComponentId = Guid.NewGuid(),
                    Quantity = 1m,
                },
            },
        };

        await _handler.Handle(command, CancellationToken.None);

        previousLineWasOpenAtCapture.Should().BeTrue();
        previous.ValidTo.Should().Be(TenantToday);
    }

    [Fact]
    public async Task Handle_InvalidCommand_DoesNotCaptureSnapshot()
    {
        UpsertProductBomCommand command = new UpsertProductBomCommand
        {
            ProductId = Guid.NewGuid(),
            Lines = new List<UpsertProductBomItem>
            {
                new UpsertProductBomItem
                {
                    ComponentId = Guid.NewGuid(),
                    Quantity = 0m,
                },
            },
        };

        Func<Task> action = () => _handler.Handle(command, CancellationToken.None);

        await action.Should().ThrowAsync<AppException>();

        _snapshotServiceMock.Verify(
            service => service.CaptureBeforeChangeAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
