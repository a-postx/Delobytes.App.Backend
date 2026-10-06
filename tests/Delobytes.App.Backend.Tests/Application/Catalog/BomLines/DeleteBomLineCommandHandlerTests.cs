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

        BomLine line = new BomLine
        {
            Id = lineId,
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

        _bomLineRepositoryMock.Verify(
            repository => repository.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
