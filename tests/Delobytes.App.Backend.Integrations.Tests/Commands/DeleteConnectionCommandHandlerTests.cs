using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Integrations.Application.Commands.DeleteConnection;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using FluentAssertions;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.Commands;

public class DeleteConnectionCommandHandlerTests
{
    private readonly Mock<IConnectionRepository> _connectionRepo = new();
    private readonly Mock<IChannelRepository> _channelRepo = new();

    private DeleteConnectionCommandHandler CreateHandler()
    {
        return new DeleteConnectionCommandHandler(
            _connectionRepo.Object,
            _channelRepo.Object);
    }

    private static Connection BuildConnection(Guid? id = null)
    {
        return new Connection
        {
            Id = id ?? Guid.NewGuid(),
            ChannelId = Guid.NewGuid(),
            Name = "Wildberries",
            ApiKey = "some-key",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    [Fact]
    public async Task Handle_ConnectionNotFound_ThrowsKeyNotFoundException()
    {
        Guid id = Guid.NewGuid();

        _connectionRepo
            .Setup(r => r.FindByIdWithChannelAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Connection?)null);

        DeleteConnectionCommandHandler handler = CreateHandler();

        Func<Task> act = () => handler.Handle(new DeleteConnectionCommand { Id = id }, CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage($"*{id}*");
    }

    [Fact]
    public async Task Handle_ValidConnection_SetsIsActiveToFalse()
    {
        Connection connection = BuildConnection();
        Guid channelId = connection.ChannelId;

        Channel catalogChannel = new Channel
        {
            Id = channelId,
            Name = "Wildberries",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _connectionRepo
            .Setup(r => r.FindByIdWithChannelAsync(connection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);

        _channelRepo
            .Setup(r => r.GetByIdAsync(channelId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(catalogChannel);

        _channelRepo
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _connectionRepo
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        DeleteConnectionCommandHandler handler = CreateHandler();
        await handler.Handle(new DeleteConnectionCommand { Id = connection.Id }, CancellationToken.None);

        connection.IsActive.Should().BeFalse();
        catalogChannel.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_CatalogChannelMissing_StillDeactivatesConnection()
    {
        Connection connection = BuildConnection();

        _connectionRepo
            .Setup(r => r.FindByIdWithChannelAsync(connection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);

        _channelRepo
            .Setup(r => r.GetByIdAsync(connection.ChannelId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Channel?)null);

        _connectionRepo
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        DeleteConnectionCommandHandler handler = CreateHandler();
        await handler.Handle(new DeleteConnectionCommand { Id = connection.Id }, CancellationToken.None);

        connection.IsActive.Should().BeFalse();

        // SaveChanges на channelRepo не должен вызываться — нечего сохранять
        _channelRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
