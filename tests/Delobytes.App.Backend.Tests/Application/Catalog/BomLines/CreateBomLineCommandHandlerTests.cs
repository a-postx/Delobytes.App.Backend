using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Catalog.Application.Commands.BomLines;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Application.Services.CostCalculation;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Contracts.Errors;
using FluentAssertions;
using FluentAssertions.Specialized;
using Moq;

namespace Delobytes.App.Backend.Tests.Application.Catalog.BomLines;

public class CreateBomLineCommandHandlerTests
{
    private readonly Mock<IBomLineRepository> _bomLineRepositoryMock;
    private readonly Mock<IComponentRepository> _componentRepositoryMock;
    private readonly Mock<IProductCostSnapshotService> _snapshotServiceMock;
    private readonly CreateBomLineCommandHandler _handler;

    public CreateBomLineCommandHandlerTests()
    {
        _bomLineRepositoryMock = new Mock<IBomLineRepository>();
        _componentRepositoryMock = new Mock<IComponentRepository>();
        _snapshotServiceMock = new Mock<IProductCostSnapshotService>();

        _bomLineRepositoryMock
            .Setup(repository => repository.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _handler = new CreateBomLineCommandHandler(
            _bomLineRepositoryMock.Object,
            _componentRepositoryMock.Object,
            _snapshotServiceMock.Object);
    }

    [Fact]
    public async Task Handle_InvalidQuantity_ThrowsDomainError()
    {
        CreateBomLineCommand command = new CreateBomLineCommand
        {
            ProductId = Guid.NewGuid(),
            ComponentId = Guid.NewGuid(),
            Quantity = 0m,
            ValidFrom = new DateOnly(2026, 1, 1),
        };

        Func<Task> action = () => _handler.Handle(command, CancellationToken.None);

        ExceptionAssertions<AppException> exception =
            await action.Should().ThrowAsync<AppException>();

        exception.Which.Code.Should().Be(CatalogErrorCodes.BomLineInvalidQuantity);

        _bomLineRepositoryMock.Verify(
            repository => repository.Add(It.IsAny<BomLine>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_ComponentDoesNotExist_ThrowsDomainError()
    {
        Guid componentId = Guid.NewGuid();

        _componentRepositoryMock
            .Setup(repository => repository.GetByIdAsync(
                componentId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Component?)null);

        CreateBomLineCommand command = new CreateBomLineCommand
        {
            ProductId = Guid.NewGuid(),
            ComponentId = componentId,
            Quantity = 2.5m,
            ValidFrom = new DateOnly(2026, 1, 1),
        };

        Func<Task> action = () => _handler.Handle(command, CancellationToken.None);

        ExceptionAssertions<AppException> exception =
            await action.Should().ThrowAsync<AppException>();

        exception.Which.Code.Should().Be(CatalogErrorCodes.BomComponentNotFound);

        _bomLineRepositoryMock.Verify(
            repository => repository.Add(It.IsAny<BomLine>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_ExistingOpenLine_ClosesItAtTheNewValidFromAndAddsVersion()
    {
        Guid productId = Guid.NewGuid();
        Guid componentId = Guid.NewGuid();
        DateOnly newValidFrom = new DateOnly(2026, 2, 1);

        BomLine previous = new BomLine
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            ComponentId = componentId,
            Quantity = 1m,
            ValidFrom = new DateOnly(2025, 1, 1),
        };

        Component component = new Component
        {
            Id = componentId,
            Name = "Material",
        };

        BomLine? addedLine = null;

        _componentRepositoryMock
            .Setup(repository => repository.GetByIdAsync(
                componentId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(component);

        _bomLineRepositoryMock
            .Setup(repository => repository.GetOpenByProductIdAsync(
                productId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BomLine> { previous });

        _bomLineRepositoryMock
            .Setup(repository => repository.Add(It.IsAny<BomLine>()))
            .Callback<BomLine>(line => addedLine = line);

        CreateBomLineCommand command = new CreateBomLineCommand
        {
            ProductId = productId,
            ComponentId = componentId,
            Quantity = 3.75m,
            ValidFrom = newValidFrom,
        };

        CreateBomLineResponse response =
            await _handler.Handle(command, CancellationToken.None);

        // Closing at the new version's start is what keeps the intervals adjacent instead of
        // overlapping or leaving a gap: the old quantity applies up to newValidFrom, the new one from it.
        previous.ValidTo.Should().Be(newValidFrom);
        previous.UpdatedAt.Should().NotBeNull();

        addedLine.Should().NotBeNull();
        addedLine!.Id.Should().Be(response.Id);
        addedLine.ProductId.Should().Be(productId);
        addedLine.ComponentId.Should().Be(componentId);
        addedLine.Quantity.Should().Be(3.75m);
        addedLine.ValidFrom.Should().Be(newValidFrom);
        addedLine.ValidTo.Should().BeNull();

        _bomLineRepositoryMock.Verify(
            repository => repository.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_ValidFromNotLaterThanTheOpenVersion_ThrowsConflict()
    {
        Guid productId = Guid.NewGuid();
        Guid componentId = Guid.NewGuid();
        DateOnly openValidFrom = new DateOnly(2025, 1, 1);

        BomLine previous = new BomLine
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            ComponentId = componentId,
            Quantity = 1m,
            ValidFrom = openValidFrom,
        };

        _componentRepositoryMock
            .Setup(repository => repository.GetByIdAsync(
                componentId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Component { Id = componentId, Name = "Material" });

        _bomLineRepositoryMock
            .Setup(repository => repository.GetOpenByProductIdAsync(
                productId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BomLine> { previous });

        CreateBomLineCommand command = new CreateBomLineCommand
        {
            ProductId = productId,
            ComponentId = componentId,
            Quantity = 2m,
            ValidFrom = openValidFrom,
        };

        Func<Task> action = () => _handler.Handle(command, CancellationToken.None);

        ExceptionAssertions<AppException> exception =
            await action.Should().ThrowAsync<AppException>();

        // An equal date would close the existing version on the day it started, producing an empty
        // interval and silently deleting a version that had been in force; an earlier one would
        // rewrite the past. Neither is a correction the caller can express here.
        exception.Which.Code.Should().Be(CatalogErrorCodes.BomLineValidFromConflict);

        previous.ValidTo.Should().BeNull();

        _bomLineRepositoryMock.Verify(
            repository => repository.Add(It.IsAny<BomLine>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_PreviousVersionNoLongerOpen_AddsVersionWithoutClosingAnything()
    {
        Guid productId = Guid.NewGuid();
        Guid componentId = Guid.NewGuid();

        _componentRepositoryMock
            .Setup(repository => repository.GetByIdAsync(
                componentId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Component { Id = componentId, Name = "Material" });

        _bomLineRepositoryMock
            .Setup(repository => repository.GetOpenByProductIdAsync(
                productId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BomLine>());

        BomLine? addedLine = null;

        _bomLineRepositoryMock
            .Setup(repository => repository.Add(It.IsAny<BomLine>()))
            .Callback<BomLine>(line => addedLine = line);

        CreateBomLineCommand command = new CreateBomLineCommand
        {
            ProductId = productId,
            ComponentId = componentId,
            Quantity = 2m,
            ValidFrom = new DateOnly(2024, 1, 1),
        };

        await _handler.Handle(command, CancellationToken.None);

        // A component previously removed from the composition can be added back with a date that
        // lies before the removal: nothing is open, so nothing has to be closed first.
        addedLine.Should().NotBeNull();
        addedLine!.ValidFrom.Should().Be(new DateOnly(2024, 1, 1));
        addedLine.ValidTo.Should().BeNull();
    }

    // ── Этап 5: автоматическая фиксация снапшота ────────────────────────────────────

    [Fact]
    public async Task Handle_ValidCommand_CapturesSnapshotForProductWithBomChangedReason()
    {
        Guid productId = Guid.NewGuid();
        Guid componentId = Guid.NewGuid();

        _componentRepositoryMock
            .Setup(repository => repository.GetByIdAsync(
                componentId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Component { Id = componentId, Name = "Material" });

        _bomLineRepositoryMock
            .Setup(repository => repository.GetOpenByProductIdAsync(
                productId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BomLine>());

        CreateBomLineCommand command = new CreateBomLineCommand
        {
            ProductId = productId,
            ComponentId = componentId,
            Quantity = 2m,
            ValidFrom = new DateOnly(2026, 5, 1),
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
    public async Task Handle_ValidCommand_CapturesSnapshotBeforeStateChanges()
    {
        Guid productId = Guid.NewGuid();
        Guid componentId = Guid.NewGuid();

        BomLine previous = new BomLine
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            ComponentId = componentId,
            Quantity = 1m,
            ValidFrom = new DateOnly(2025, 1, 1),
        };

        bool previousLineWasOpenAtCapture = false;
        bool newLineExistedAtCapture = false;
        int addedLineCount = 0;

        _componentRepositoryMock
            .Setup(repository => repository.GetByIdAsync(
                componentId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Component { Id = componentId, Name = "Material" });

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
                    // The snapshot has to reflect the composition as it was, so neither the closing
                    // of the previous version nor the insertion of the new one may have happened yet.
                    previousLineWasOpenAtCapture = previous.ValidTo == null;
                    newLineExistedAtCapture = addedLineCount > 0;
                })
            .Returns(Task.CompletedTask);

        CreateBomLineCommand command = new CreateBomLineCommand
        {
            ProductId = productId,
            ComponentId = componentId,
            Quantity = 3m,
            ValidFrom = new DateOnly(2026, 5, 1),
        };

        await _handler.Handle(command, CancellationToken.None);

        previousLineWasOpenAtCapture.Should().BeTrue();
        newLineExistedAtCapture.Should().BeFalse();
        previous.ValidTo.Should().Be(new DateOnly(2026, 5, 1));
    }

    [Fact]
    public async Task Handle_InvalidCommand_DoesNotCaptureSnapshot()
    {
        CreateBomLineCommand command = new CreateBomLineCommand
        {
            ProductId = Guid.NewGuid(),
            ComponentId = Guid.NewGuid(),
            Quantity = -1m,
            ValidFrom = new DateOnly(2026, 1, 1),
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
