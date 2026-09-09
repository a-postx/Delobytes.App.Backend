using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Integrations.Application.Commands.DeleteConnection;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using FluentAssertions;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.Commands;

public class DeleteConnectionCommandHandlerTests
{
    private readonly Mock<IConnectionRepository> _connectionRepo = new ();
    private readonly Mock<IEventPublisher> _eventPublisher = new ();

    private DeleteConnectionCommandHandler CreateHandler()
    {
        return new DeleteConnectionCommandHandler(
            _connectionRepo.Object,
            _eventPublisher.Object);
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
            .Setup(r => r.FindByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Connection?)null);

        DeleteConnectionCommandHandler handler = CreateHandler();

        Func<Task> act = () => handler.Handle(new DeleteConnectionCommand { Id = id }, CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage($"*{id}*");
    }

    [Fact]
    public async Task Handle_ValidConnection_SetsIsActiveToFalseAndPublishesEvent()
    {
        Connection connection = BuildConnection();
        ConnectionDeactivatedEvent? publishedEvent = null;

        _connectionRepo
            .Setup(r => r.FindByIdWithChannelAsync(connection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);

        _connectionRepo
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _eventPublisher
            .Setup(p => p.PublishAsync(It.IsAny<ConnectionDeactivatedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<ConnectionDeactivatedEvent, CancellationToken>((e, _) => publishedEvent = e)
            .Returns(Task.CompletedTask);

        DeleteConnectionCommandHandler handler = CreateHandler();


        await handler.Handle(new DeleteConnectionCommand { Id = connection.Id }, CancellationToken.None);


        connection.IsActive.Should().BeFalse();

        publishedEvent.Should().NotBeNull();
        publishedEvent!.ChannelId.Should().Be(connection.ChannelId);
    }

    [Fact]
    public async Task Handle_ValidConnection_EventPublishedAfterSave()
    {
        Connection connection = BuildConnection();
        bool savedBeforePublish = false;

        _connectionRepo
            .Setup(r => r.FindByIdWithChannelAsync(connection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);

        _connectionRepo
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => savedBeforePublish = true)
            .ReturnsAsync(1);

        _eventPublisher
            .Setup(p => p.PublishAsync(It.IsAny<ConnectionDeactivatedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<ConnectionDeactivatedEvent, CancellationToken>((_, _) =>
            {
                // verify save already happened before publish
                savedBeforePublish.Should().BeTrue();
            })
            .Returns(Task.CompletedTask);

        DeleteConnectionCommandHandler handler = CreateHandler();
        await handler.Handle(new DeleteConnectionCommand { Id = connection.Id }, CancellationToken.None);
    }
}
