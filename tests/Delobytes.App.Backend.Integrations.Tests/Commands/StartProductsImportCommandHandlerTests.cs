using System;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Integrations.Application;
using Delobytes.App.Backend.Integrations.Application.Commands;
using Delobytes.App.Backend.Integrations.Application.Events;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Jobs;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.Commands;

public class StartProductsImportCommandHandlerTests
{
    private readonly Mock<IConnectionRepository> _connectionRepo = new();
    private readonly Mock<IJobRunRepository> _jobRunRepo = new();
    private readonly Mock<ISyncCheckpointRepository> _syncCheckpointRepo = new();
    private readonly Mock<IEventPublisher> _eventPublisher = new();
    private readonly Mock<IChannelJobTypeRegistry> _jobTypeRegistry = new();
    private readonly Mock<IUserContext> _userContext = new();
    private readonly Mock<ILogger<StartProductsImportCommandHandler>> _logger = new();

    private StartProductsImportCommandHandler CreateHandler()
    {
        SetupKnownChannels();

        JobRunStarter jobRunStarter = new JobRunStarter(
            _jobRunRepo.Object,
            TimeProvider.System,
            NullLogger<JobRunStarter>.Instance);

        return new StartProductsImportCommandHandler(
            _connectionRepo.Object,
            _jobRunRepo.Object,
            _syncCheckpointRepo.Object,
            _eventPublisher.Object,
            jobRunStarter,
            _jobTypeRegistry.Object,
            _userContext.Object,
            _logger.Object);
    }

    /// <summary>
    /// Mirrors the real registry for the channels these tests exercise: each channel serves exactly
    /// one products-import job type, and a channel that serves none yields an empty list.
    /// </summary>
    private void SetupKnownChannels()
    {
        _jobTypeRegistry
            .Setup(r => r.GetJobTypes(It.IsAny<string?>(), ChannelJobKind.ProductsImport))
            .Returns<string?, ChannelJobKind>((channelCode, _) => channelCode switch
            {
                "wildberries" => new[] { JobType.ProductsImport },
                "ozon" => new[] { JobType.OzonProductsImport },
                _ => Array.Empty<JobType>(),
            });
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

    private void SetupHappyPathUpToJobCreation(Connection connection)
    {
        _connectionRepo
            .Setup(r => r.FindByIdWithTemplateAsync(connection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);

        _jobRunRepo
            .Setup(r => r.HasActiveJobAsync(connection.Id, JobType.ProductsImport, It.IsAny<CancellationToken>()))
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
    public async Task Handle_ChannelWithoutProductsImportKind_ThrowsInvalidOperationException()
    {
        Connection connection = BuildConnection(templateCode: "yandex.kit");

        _connectionRepo
            .Setup(r => r.FindByIdWithTemplateAsync(connection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);

        StartProductsImportCommandHandler handler = CreateHandler();

        Func<Task> act = () => handler.Handle(
            new StartProductsImportCommand { ConnectionId = connection.Id },
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*Импорт карточек товаров не поддерживается для канала 'yandex.kit'*");
    }

    [Fact]
    public async Task Handle_ChannelWithSeveralProductsJobTypes_ThrowsInsteadOfGuessing()
    {
        Connection connection = BuildConnection(templateCode: "ozon");

        _connectionRepo
            .Setup(r => r.FindByIdWithTemplateAsync(connection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);

        StartProductsImportCommandHandler handler = CreateHandler();

        // Set up after CreateHandler so this channel-specific answer wins over the defaults.
        _jobTypeRegistry
            .Setup(r => r.GetJobTypes("ozon", ChannelJobKind.ProductsImport))
            .Returns(new[] { JobType.OzonProductsImport, JobType.ProductsImport });

        Func<Task> act = () => handler.Handle(
            new StartProductsImportCommand { ConnectionId = connection.Id },
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*ozon*несколько*");
    }

    [Fact]
    public async Task Handle_OzonConnection_CreatesRunOfTheResolvedJobType()
    {
        // The registry, not this handler, owns the mapping from channel to products job type.
        // Every call that persists or reads per-type state must use the resolved value.
        Connection connection = BuildConnection(templateCode: "ozon");
        JobRun? savedJobRun = null;
        SyncCheckpoint? savedCheckpoint = null;

        _connectionRepo
            .Setup(r => r.FindByIdWithTemplateAsync(connection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);

        _jobRunRepo
            .Setup(r => r.HasActiveJobAsync(connection.Id, JobType.OzonProductsImport, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _jobRunRepo
            .Setup(r => r.CreateAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()))
            .Callback<JobRun, CancellationToken>((j, _) => savedJobRun = j)
            .ReturnsAsync((JobRun j, CancellationToken _) => j);

        _syncCheckpointRepo
            .Setup(r => r.GetAsync(connection.Id, JobType.OzonProductsImport, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncCheckpoint?)null);

        _syncCheckpointRepo
            .Setup(r => r.UpsertAsync(It.IsAny<SyncCheckpoint>(), It.IsAny<CancellationToken>()))
            .Callback<SyncCheckpoint, CancellationToken>((c, _) => savedCheckpoint = c)
            .Returns(Task.CompletedTask);

        _userContext.Setup(u => u.UserId).Returns((Guid?)null);

        StartProductsImportCommandHandler handler = CreateHandler();
        await handler.Handle(
            new StartProductsImportCommand { ConnectionId = connection.Id },
            CancellationToken.None);

        savedJobRun.Should().NotBeNull();
        savedJobRun!.JobType.Should().Be(JobType.OzonProductsImport);

        savedCheckpoint.Should().NotBeNull();
        savedCheckpoint!.JobType.Should().Be(JobType.OzonProductsImport);

        _jobRunRepo.Verify(
            r => r.HasActiveJobAsync(connection.Id, JobType.OzonProductsImport, It.IsAny<CancellationToken>()),
            Times.Once);

        _jobRunRepo.Verify(
            r => r.HasActiveJobAsync(connection.Id, JobType.ProductsImport, It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_DuplicateActiveJob_ThrowsConflictException()
    {
        Connection connection = BuildConnection();

        _connectionRepo
            .Setup(r => r.FindByIdWithTemplateAsync(connection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);

        _jobRunRepo
            .Setup(r => r.HasActiveJobAsync(connection.Id, JobType.ProductsImport, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        StartProductsImportCommandHandler handler = CreateHandler();

        Func<Task> act = () => handler.Handle(
            new StartProductsImportCommand { ConnectionId = connection.Id },
            CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("*активная задача*");
    }

    [Fact]
    public async Task Handle_ValidRequest_CreatesJobRunAndProductsImportJobWithPendingStatus()
    {
        Connection connection = BuildConnection();
        Guid userId = Guid.NewGuid();
        JobRun? savedJobRun = null;
        ProductsImportJob? savedProductsImportJob = null;

        SetupHappyPathUpToJobCreation(connection);

        _jobRunRepo
            .Setup(r => r.CreateAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()))
            .Callback<JobRun, CancellationToken>((j, _) => savedJobRun = j)
            .ReturnsAsync((JobRun j, CancellationToken _) => j);

        _jobRunRepo
            .Setup(r => r.CreateProductsImportJobAsync(It.IsAny<ProductsImportJob>(), It.IsAny<CancellationToken>()))
            .Callback<ProductsImportJob, CancellationToken>((j, _) => savedProductsImportJob = j)
            .Returns(Task.CompletedTask);

        _syncCheckpointRepo
            .Setup(r => r.GetAsync(connection.Id, JobType.ProductsImport, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncCheckpoint?)null);

        _userContext.Setup(u => u.UserId).Returns(userId);

        StartProductsImportCommandHandler handler = CreateHandler();
        StartProductsImportResponse result = await handler.Handle(
            new StartProductsImportCommand { ConnectionId = connection.Id },
            CancellationToken.None);

        result.Should().NotBeNull();
        result.JobRunId.Should().NotBeEmpty();

        savedJobRun.Should().NotBeNull();
        savedJobRun!.Status.Should().Be(JobStatus.Pending);
        savedJobRun.JobType.Should().Be(JobType.ProductsImport);
        savedJobRun.ConnectionId.Should().Be(connection.Id);
        savedJobRun.RequestedByUserId.Should().Be(userId);

        savedProductsImportJob.Should().NotBeNull();
        savedProductsImportJob!.JobRunId.Should().Be(savedJobRun.Id);
    }

    [Fact]
    public async Task Handle_NoExistingCheckpoint_CreatesCheckpointWithThirtyDayBackfill()
    {
        Connection connection = BuildConnection();
        SyncCheckpoint? savedCheckpoint = null;

        SetupHappyPathUpToJobCreation(connection);

        _syncCheckpointRepo
            .Setup(r => r.GetAsync(connection.Id, JobType.ProductsImport, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncCheckpoint?)null);

        _syncCheckpointRepo
            .Setup(r => r.UpsertAsync(It.IsAny<SyncCheckpoint>(), It.IsAny<CancellationToken>()))
            .Callback<SyncCheckpoint, CancellationToken>((c, _) => savedCheckpoint = c)
            .Returns(Task.CompletedTask);

        _userContext.Setup(u => u.UserId).Returns((Guid?)null);

        StartProductsImportCommandHandler handler = CreateHandler();
        await handler.Handle(
            new StartProductsImportCommand { ConnectionId = connection.Id },
            CancellationToken.None);

        savedCheckpoint.Should().NotBeNull();
        savedCheckpoint!.ConnectionId.Should().Be(connection.Id);
        savedCheckpoint.JobType.Should().Be(JobType.ProductsImport);
        savedCheckpoint.LastChangedAt.Should().BeNull();
        savedCheckpoint.LastSuccessAt.Should().BeCloseTo(DateTimeOffset.UtcNow.AddDays(-30), TimeSpan.FromMinutes(1));
        savedCheckpoint.LastJobRunId.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ExistingCheckpoint_DoesNotOverwriteWatermark()
    {
        Connection connection = BuildConnection();
        SyncCheckpoint existingCheckpoint = new SyncCheckpoint
        {
            Id = Guid.NewGuid(),
            ConnectionId = connection.Id,
            JobType = JobType.ProductsImport,
            LastChangedAt = DateTimeOffset.UtcNow.AddHours(-2),
            LastSuccessAt = DateTimeOffset.UtcNow.AddHours(-1),
            LastJobRunId = Guid.NewGuid(),
        };

        SetupHappyPathUpToJobCreation(connection);

        _syncCheckpointRepo
            .Setup(r => r.GetAsync(connection.Id, JobType.ProductsImport, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingCheckpoint);

        _userContext.Setup(u => u.UserId).Returns((Guid?)null);

        StartProductsImportCommandHandler handler = CreateHandler();
        await handler.Handle(
            new StartProductsImportCommand { ConnectionId = connection.Id },
            CancellationToken.None);

        _syncCheckpointRepo.Verify(
            r => r.UpsertAsync(It.IsAny<SyncCheckpoint>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_ValidRequest_PublishesProductsImportRequestedEvent()
    {
        Connection connection = BuildConnection();
        ProductsImportRequestedEvent? publishedEvent = null;

        SetupHappyPathUpToJobCreation(connection);

        _syncCheckpointRepo
            .Setup(r => r.GetAsync(connection.Id, JobType.ProductsImport, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncCheckpoint
            {
                Id = Guid.NewGuid(),
                ConnectionId = connection.Id,
                JobType = JobType.ProductsImport,
                LastSuccessAt = DateTimeOffset.UtcNow,
            });

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
        publishedEvent.JobRunId.Should().Be(result.JobRunId);
    }

    [Fact]
    public async Task Handle_PublishFails_ReturnsResponseWithoutThrowing()
    {
        Connection connection = BuildConnection();

        SetupHappyPathUpToJobCreation(connection);

        _syncCheckpointRepo
            .Setup(r => r.GetAsync(connection.Id, JobType.ProductsImport, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncCheckpoint
            {
                Id = Guid.NewGuid(),
                ConnectionId = connection.Id,
                JobType = JobType.ProductsImport,
                LastSuccessAt = DateTimeOffset.UtcNow,
            });

        _eventPublisher
            .Setup(p => p.PublishAsync(It.IsAny<ProductsImportRequestedEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("RabbitMQ недоступен"));

        _userContext.Setup(u => u.UserId).Returns((Guid?)null);

        StartProductsImportCommandHandler handler = CreateHandler();

        // Публикация упала, но JobRun уже сохранён — exception не должен пробрасываться наружу.
        Func<Task> act = () => handler.Handle(
            new StartProductsImportCommand { ConnectionId = connection.Id },
            CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}
