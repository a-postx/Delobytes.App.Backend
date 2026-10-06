using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Catalog.Application.Commands.BomLines.CreateBomLine;
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

        exception.Which.Code.Should().Be(ErrorCodes.Catalog.BomLineInvalidQuantity);

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

        exception.Which.Code.Should().Be(ErrorCodes.Catalog.BomComponentNotFound);

        _bomLineRepositoryMock.Verify(
            repository => repository.Add(It.IsAny<BomLine>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_ExistingActiveLine_DeactivatesItAndAddsNewVersion()
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
            IsActive = true,
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
            .Setup(repository => repository.GetActiveByProductIdAsync(
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
            ValidFrom = new DateOnly(2026, 2, 1),
        };

        CreateBomLineResponse response =
            await _handler.Handle(command, CancellationToken.None);

        previous.IsActive.Should().BeFalse();
        previous.UpdatedAt.Should().NotBeNull();

        addedLine.Should().NotBeNull();
        addedLine!.Id.Should().Be(response.Id);
        addedLine.ProductId.Should().Be(productId);
        addedLine.ComponentId.Should().Be(componentId);
        addedLine.Quantity.Should().Be(3.75m);
        addedLine.ValidFrom.Should().Be(new DateOnly(2026, 2, 1));
        addedLine.IsActive.Should().BeTrue();

        _bomLineRepositoryMock.Verify(
            repository => repository.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── Этап 5: автоматическая фиксация снапшота ─────────────────────────────────────

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
            .Setup(repository => repository.GetActiveByProductIdAsync(
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
            IsActive = true,
        };

        bool previousLineWasActiveAtCapture = false;
        bool newLineExistedAtCapture = false;
        int addedLineCount = 0;

        _componentRepositoryMock
            .Setup(repository => repository.GetByIdAsync(
                componentId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Component { Id = componentId, Name = "Material" });

        _bomLineRepositoryMock
            .Setup(repository => repository.GetActiveByProductIdAsync(
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
                    // The snapshot has to reflect the composition as it was, so neither the
                    // deactivation of the previous line nor the insertion of the new one may
                    // have happened yet.
                    previousLineWasActiveAtCapture = previous.IsActive;
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

        previousLineWasActiveAtCapture.Should().BeTrue();
        newLineExistedAtCapture.Should().BeFalse();
        previous.IsActive.Should().BeFalse();
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
