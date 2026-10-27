using System;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Integrations.Application;
using Delobytes.App.Backend.Integrations.Application.Commands;
using Delobytes.App.Backend.Integrations.Application.Events;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.Commands;

public class StartOrdersImportCommandHandlerTests
{
    private readonly Mock<IConnectionRepository> _connectionRepo = new();
    private readonly Mock<IJobRunRepository> _jobRunRepo = new();
    private readonly Mock<IEventPublisher> _eventPublisher = new();
    private readonly Mock<ILogger<StartOrdersImportCommandHandler>> _logger = new();

    private StartOrdersImportCommandHandler CreateHandler()
    {
        return new StartOrdersImportCommandHandler(
            _connectionRepo.Object,
            _jobRunRepo.Object,
            _eventPublisher.Object,
            _logger.Object);
    }

    private static Connection BuildConnection(bool isActive = true)
    {
        SystemChannelTemplate template = new SystemChannelTemplate
        {
            Id = Guid.NewGuid(),
            Code = "wildberries",
            DisplayName = "Wildberries",
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

    private void SetupHappyPathUpToJobCreation(Connection connection)
    {
        _connectionRepo
            .Setup(r => r.FindByIdAsync(connection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);

        _jobRunRepo
            .Setup(r => r.HasActiveJobAsync(connection.Id, JobType.OrdersImport, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _jobRunRepo
            .Setup(r => r.CreateAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((JobRun j, CancellationToken _) => j);
    }

    [Fact]
    public async Task Handle_ConnectionNotFound_ThrowsKeyNotFoundException()
    {
        Guid connectionId = Guid.NewGuid();

        _connectionRepo
            .Setup(r => r.FindByIdAsync(connectionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Connection?)null);

        StartOrdersImportCommandHandler handler = CreateHandler();

        Func<Task> act = () => handler.Handle(
            new StartOrdersImportCommand { ConnectionId = connectionId },
            CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage($"*{connectionId}*");
    }

    [Fact]
    public async Task Handle_ConnectionInactive_ThrowsInvalidOperationException()
    {
        Connection connection = BuildConnection(isActive: false);

        _connectionRepo
            .Setup(r => r.FindByIdAsync(connection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);

        StartOrdersImportCommandHandler handler = CreateHandler();

        Func<Task> act = () => handler.Handle(
            new StartOrdersImportCommand { ConnectionId = connection.Id },
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*неакт*");
    }

    [Fact]
    public async Task Handle_DuplicateActiveJob_ThrowsConflictException()
    {
        Connection connection = BuildConnection();

        _connectionRepo
            .Setup(r => r.FindByIdAsync(connection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);

        _jobRunRepo
            .Setup(r => r.HasActiveJobAsync(connection.Id, JobType.OrdersImport, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        StartOrdersImportCommandHandler handler = CreateHandler();

        Func<Task> act = () => handler.Handle(
            new StartOrdersImportCommand { ConnectionId = connection.Id },
            CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("*активная задача*");
    }

    [Fact]
    public async Task Handle_ValidRequest_CreatesJobRunWithPendingStatus()
    {
        Connection connection = BuildConnection();
        JobRun? savedJobRun = null;

        SetupHappyPathUpToJobCreation(connection);

        _jobRunRepo
            .Setup(r => r.CreateAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()))
            .Callback<JobRun, CancellationToken>((j, _) => savedJobRun = j)
            .ReturnsAsync((JobRun j, CancellationToken _) => j);

        StartOrdersImportCommandHandler handler = CreateHandler();
        StartOrdersImportResponse result = await handler.Handle(
            new StartOrdersImportCommand { ConnectionId = connection.Id },
            CancellationToken.None);

        result.Should().NotBeNull();
        result.JobRunId.Should().NotBeEmpty();

        savedJobRun.Should().NotBeNull();
        savedJobRun!.Status.Should().Be(JobStatus.Pending);
        savedJobRun.JobType.Should().Be(JobType.OrdersImport);
        savedJobRun.ConnectionId.Should().Be(connection.Id);
        savedJobRun.RequestedByUserId.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ValidRequest_PublishesOrdersImportRequestedEventWithJobRunId()
    {
        Connection connection = BuildConnection();
        OrdersImportRequestedEvent? publishedEvent = null;

        SetupHappyPathUpToJobCreation(connection);

        _eventPublisher
            .Setup(p => p.PublishAsync(It.IsAny<OrdersImportRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<OrdersImportRequestedEvent, CancellationToken>((e, _) => publishedEvent = e)
            .Returns(Task.CompletedTask);

        StartOrdersImportCommandHandler handler = CreateHandler();
        StartOrdersImportResponse result = await handler.Handle(
            new StartOrdersImportCommand { ConnectionId = connection.Id },
            CancellationToken.None);

        publishedEvent.Should().NotBeNull();
        publishedEvent!.JobRunId.Should().Be(result.JobRunId);
        publishedEvent.ConnectionId.Should().Be(connection.Id);
    }

    [Fact]
    public async Task Handle_EventPublishThrows_StillReturnsResponseWithPendingJobRun()
    {
        Connection connection = BuildConnection();

        SetupHappyPathUpToJobCreation(connection);

        _eventPublisher
            .Setup(p => p.PublishAsync(It.IsAny<OrdersImportRequestedEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("broker unavailable"));

        StartOrdersImportCommandHandler handler = CreateHandler();

        StartOrdersImportResponse result = await handler.Handle(
            new StartOrdersImportCommand { ConnectionId = connection.Id },
            CancellationToken.None);

        result.Should().NotBeNull();
        result.JobRunId.Should().NotBeEmpty();
    }
}
