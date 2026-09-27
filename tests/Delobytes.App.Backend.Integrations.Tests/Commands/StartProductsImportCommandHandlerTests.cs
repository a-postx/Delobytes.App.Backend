using System;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Integrations.Application;
using Delobytes.App.Backend.Integrations.Application.Commands.StartProductsImport;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.Commands;

public class StartProductsImportCommandHandlerTests
{
    private readonly Mock<IConnectionRepository> _connectionRepo = new();
    private readonly Mock<ISyncJobRepository> _syncJobRepo = new();
    private readonly Mock<IEventPublisher> _eventPublisher = new();
    private readonly Mock<IUserContext> _userContext = new();
    private readonly Mock<ILogger<StartProductsImportCommandHandler>> _logger = new();

    private StartProductsImportCommandHandler CreateHandler()
    {
        return new StartProductsImportCommandHandler(
            _connectionRepo.Object,
            _syncJobRepo.Object,
            _eventPublisher.Object,
            _userContext.Object,
            _logger.Object);
    }

    private static SystemChannelTemplate BuildWildberriesTemplate()
    {
        return new SystemChannelTemplate
        {
            Id = Guid.NewGuid(),
            Code = "wildberries",
            DisplayName = "Wildberries",
            ApiBaseUrl = "https://suppliers-api.wildberries.ru",
            ApiVersion = "v3",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    private static Connection BuildConnection(string templateCode = "wildberries", bool isActive = true)
    {
        SystemChannelTemplate template = new SystemChannelTemplate
        {
            Id = Guid.NewGuid(),
            Code = templateCode,
            DisplayName = templateCode,
            ApiBaseUrl = "https://example.com",
            ApiVersion = "v3",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        return new Connection
        {
            Id = Guid.NewGuid(),
            ChannelId = Guid.NewGuid(),
            SystemChannelTemplateId = template.Id,
            Name = "Test",
            ApiKey = "test-api-key",
            IsActive = isActive,
            CreatedAt = DateTimeOffset.UtcNow,
            SystemChannelTemplate = template,
        };
    }

    [Fact]
    public async Task Handle_ConnectionNotFound_ThrowsKeyNotFoundException()
    {
        Guid connectionId = Guid.NewGuid();

        _connectionRepo
            .Setup(r => r.FindByIdWithTemplateAsync(connectionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Connection?)null);

        StartProductsImportCommandHandler handler = CreateHandler();

        Func<Task> act = () => handler.Handle(
            new StartProductsImportCommand { ConnectionId = connectionId },
            CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage($"*{connectionId}*");
    }

    [Fact]
    public async Task Handle_ConnectionInactive_ThrowsInvalidOperationException()
    {
        Connection connection = BuildConnection(isActive: false);

        _connectionRepo
            .Setup(r => r.FindByIdWithTemplateAsync(connection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);

        StartProductsImportCommandHandler handler = CreateHandler();

        Func<Task> act = () => handler.Handle(
            new StartProductsImportCommand { ConnectionId = connection.Id },
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*неакт*");
    }

    [Fact]
    public async Task Handle_NonWildberriesConnection_ThrowsInvalidOperationException()
    {
        Connection connection = BuildConnection(templateCode: "ozon");

        _connectionRepo
            .Setup(r => r.FindByIdWithTemplateAsync(connection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);

        StartProductsImportCommandHandler handler = CreateHandler();

        Func<Task> act = () => handler.Handle(
            new StartProductsImportCommand { ConnectionId = connection.Id },
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Handle_DuplicateActiveJob_ThrowsConflictException()
    {
        Connection connection = BuildConnection();

        _connectionRepo
            .Setup(r => r.FindByIdWithTemplateAsync(connection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);

        _syncJobRepo
            .Setup(r => r.HasActivePendingOrRunningJobForConnectionAsync(connection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        StartProductsImportCommandHandler handler = CreateHandler();

        Func<Task> act = () => handler.Handle(
            new StartProductsImportCommand { ConnectionId = connection.Id },
            CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("*активная задача*");
    }

    [Fact]
    public async Task Handle_ValidRequest_CreatesSyncJobWithPendingStatus()
    {
        Connection connection = BuildConnection();
        SyncJob? savedJob = null;
        Guid userId = Guid.NewGuid();

        _connectionRepo
            .Setup(r => r.FindByIdWithTemplateAsync(connection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);

        _syncJobRepo
            .Setup(r => r.HasActivePendingOrRunningJobForConnectionAsync(connection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _syncJobRepo
            .Setup(r => r.Add(It.IsAny<SyncJob>()))
            .Callback<SyncJob>(j => savedJob = j);

        _syncJobRepo
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _eventPublisher
            .Setup(p => p.PublishAsync(It.IsAny<ProductsImportRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _userContext.Setup(u => u.UserId).Returns(userId);

        StartProductsImportCommandHandler handler = CreateHandler();
        StartProductsImportResponse result = await handler.Handle(
            new StartProductsImportCommand { ConnectionId = connection.Id },
            CancellationToken.None);

        result.Should().NotBeNull();
        result.SyncJobId.Should().NotBeEmpty();
        savedJob.Should().NotBeNull();
        savedJob!.Status.Should().Be(SyncJobStatus.Pending);
        savedJob.JobType.Should().Be(JobType.ProductsImport);
        savedJob.ConnectionId.Should().Be(connection.Id);
        savedJob.RequestedByUserId.Should().Be(userId);
    }

    [Fact]
    public async Task Handle_ValidRequest_PublishesProductsImportRequestedEvent()
    {
        Connection connection = BuildConnection();
        ProductsImportRequestedEvent? publishedEvent = null;

        _connectionRepo
            .Setup(r => r.FindByIdWithTemplateAsync(connection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);

        _syncJobRepo
            .Setup(r => r.HasActivePendingOrRunningJobForConnectionAsync(connection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _syncJobRepo
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _eventPublisher
            .Setup(p => p.PublishAsync(It.IsAny<ProductsImportRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<ProductsImportRequestedEvent, CancellationToken>((e, _) => publishedEvent = e)
            .Returns(Task.CompletedTask);

        _userContext.Setup(u => u.UserId).Returns((Guid?)null);

        StartProductsImportCommandHandler handler = CreateHandler();
        StartProductsImportResponse result = await handler.Handle(
            new StartProductsImportCommand { ConnectionId = connection.Id },
            CancellationToken.None);

        publishedEvent.Should().NotBeNull();
        publishedEvent!.ConnectionId.Should().Be(connection.Id);
        publishedEvent.SyncJobId.Should().Be(result.SyncJobId);
    }

    [Fact]
    public async Task Handle_PublishFails_ReturnsResponseWithoutThrowing()
    {
        Connection connection = BuildConnection();

        _connectionRepo
            .Setup(r => r.FindByIdWithTemplateAsync(connection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);

        _syncJobRepo
            .Setup(r => r.HasActivePendingOrRunningJobForConnectionAsync(connection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _syncJobRepo
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _eventPublisher
            .Setup(p => p.PublishAsync(It.IsAny<ProductsImportRequestedEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("RabbitMQ недоступен"));

        _userContext.Setup(u => u.UserId).Returns((Guid?)null);

        StartProductsImportCommandHandler handler = CreateHandler();

        // Публикация упала, но SyncJob уже сохранён — exception не должен пробрасываться наружу.
        Func<Task> act = () => handler.Handle(
            new StartProductsImportCommand { ConnectionId = connection.Id },
            CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}
