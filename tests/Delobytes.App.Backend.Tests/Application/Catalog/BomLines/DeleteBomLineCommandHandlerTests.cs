using Delobytes.App.Backend.Catalog.Application.Commands.BomLines.DeleteBomLine;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Contracts.Errors;
using FluentAssertions;
using FluentAssertions.Specialized;
using Moq;

namespace Delobytes.App.Backend.Tests.Application.Catalog.BomLines;

public class DeleteBomLineCommandHandlerTests
{
    private readonly Mock<IBomLineRepository> _bomLineRepositoryMock;
    private readonly DeleteBomLineCommandHandler _handler;

    public DeleteBomLineCommandHandlerTests()
    {
        _bomLineRepositoryMock = new Mock<IBomLineRepository>();

        _bomLineRepositoryMock
            .Setup(repository => repository.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _handler = new DeleteBomLineCommandHandler(
            _bomLineRepositoryMock.Object);
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
    public async Task Handle_InactiveLine_ThrowsNotFoundError()
    {
        Guid lineId = Guid.NewGuid();

        BomLine line = new BomLine
        {
            Id = lineId,
            IsActive = false,
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

        ExceptionAssertions<AppException> inactiveLineException =
            await action.Should().ThrowAsync<AppException>();

        inactiveLineException.Which.Code
            .Should()
            .Be(ErrorCodes.Catalog.BomLineNotFound);
    }

    [Fact]
    public async Task Handle_ActiveLine_SoftDeletesAndSavesIt()
    {
        Guid lineId = Guid.NewGuid();
        DateOnly validFrom = new DateOnly(2025, 1, 1);

        BomLine line = new BomLine
        {
            Id = lineId,
            ValidFrom = validFrom,
            IsActive = true,
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
        line.IsActive.Should().BeFalse();
        line.UpdatedAt.Should().NotBeNull();

        // Closing the interval is the part that removes the line from the cost calculation: clearing
        // IsActive alone would only hide it from the composition editor.
        line.ValidTo.Should().Be(DateOnly.FromDateTime(DateTime.UtcNow));

        _bomLineRepositoryMock.Verify(
            repository => repository.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_LineScheduledForTheFuture_ClosesItWithEmptyInterval()
    {
        Guid lineId = Guid.NewGuid();
        DateOnly scheduledFrom = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);

        BomLine line = new BomLine
        {
            Id = lineId,
            ValidFrom = scheduledFrom,
            IsActive = true,
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

        // An inverted interval would leave the line priced on every date from the removal onwards, so
        // the end is clamped to the start instead.
        line.ValidTo.Should().Be(scheduledFrom);
        line.IsActive.Should().BeFalse();
    }
}
