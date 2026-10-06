using Delobytes.App.Backend.Catalog.Application.Commands.BomLines.UpsertProductBom;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Application.Services.CostCalculation;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Contracts.Errors;
using FluentAssertions;
using FluentAssertions.Specialized;
using Moq;

namespace Delobytes.App.Backend.Tests.Application.Catalog.BomLines;

public class UpsertProductBomCommandHandlerTests
{
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
            _snapshotServiceMock.Object);
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

        exception.Which.Code.Should().Be(ErrorCodes.Catalog.BomLineInvalidQuantity);
    }

    [Fact]
    public async Task Handle_ReplacesAllActiveLinesWithNewLines()
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
            IsActive = true,
        };

        List<BomLine> addedLines = new List<BomLine>();

        _bomLineRepositoryMock
            .Setup(repository => repository.GetActiveByProductIdAsync(
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

        previous.IsActive.Should().BeFalse();
        previous.UpdatedAt.Should().NotBeNull();

        // The version being replaced ends on the date its replacements start, so the two never
        // overlap in the interval the cost calculation resolves against.
        previous.ValidTo.Should().Be(DateOnly.FromDateTime(DateTime.UtcNow));

        addedLines.Should().HaveCount(2);
        addedLines.Should().OnlyContain(line =>
            line.ProductId == productId &&
            line.IsActive);

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
            .OnlyContain(date => date == DateOnly.FromDateTime(DateTime.UtcNow));

        _bomLineRepositoryMock.Verify(
            repository => repository.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_ComponentRemovedFromComposition_ClosesItsLineWithoutSuccessor()
    {
        Guid productId = Guid.NewGuid();
        Guid removedComponentId = Guid.NewGuid();
        Guid keptComponentId = Guid.NewGuid();

        BomLine lineToRemove = new BomLine
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            ComponentId = removedComponentId,
            Quantity = 3m,
            ValidFrom = new DateOnly(2025, 6, 1),
            IsActive = true,
        };

        BomLine lineToKeep = new BomLine
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            ComponentId = keptComponentId,
            Quantity = 1m,
            ValidFrom = new DateOnly(2025, 6, 1),
            IsActive = true,
        };

        _bomLineRepositoryMock
            .Setup(repository => repository.GetActiveByProductIdAsync(
                productId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BomLine> { lineToRemove, lineToKeep });

        // The user keeps one component and removes the other, which is what the bug report describes.
        UpsertProductBomCommand command = new UpsertProductBomCommand
        {
            ProductId = productId,
            Lines = new List<UpsertProductBomItem>
            {
                new UpsertProductBomItem
                {
                    ComponentId = keptComponentId,
                    Quantity = 1m,
                },
            },
        };

        await _handler.Handle(command, CancellationToken.None);

        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Both lines end today: the removed component gets no successor, so once the interval is
        // closed it drops out of the cost calculation instead of being priced forever.
        lineToRemove.ValidTo.Should().Be(today);
        lineToRemove.IsActive.Should().BeFalse();

        lineToKeep.ValidTo.Should().Be(today);
        lineToKeep.IsActive.Should().BeFalse();
    }

    // ── Этап 5: автоматическая фиксация снапшота ─────────────────────────────────────

    [Fact]
    public async Task Handle_ValidCommand_CapturesSnapshotForProductWithBomChangedReason()
    {
        Guid productId = Guid.NewGuid();

        _bomLineRepositoryMock
            .Setup(repository => repository.GetActiveByProductIdAsync(
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
    public async Task Handle_ValidCommand_CapturesSnapshotBeforeActiveLinesAreDeactivated()
    {
        Guid productId = Guid.NewGuid();

        BomLine previous = new BomLine
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            ComponentId = Guid.NewGuid(),
            Quantity = 5m,
            ValidFrom = new DateOnly(2025, 1, 1),
            IsActive = true,
        };

        bool previousLineWasActiveAtCapture = false;
        int addedLineCount = 0;

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
                    // The full replacement must not have started yet, otherwise the snapshot
                    // would capture the new composition instead of the one being replaced.
                    previousLineWasActiveAtCapture = previous.IsActive;
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

        previousLineWasActiveAtCapture.Should().BeTrue();
        previous.IsActive.Should().BeFalse();
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
