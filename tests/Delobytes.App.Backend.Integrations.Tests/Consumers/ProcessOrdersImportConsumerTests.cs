using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Integrations.Application.Events;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Application.Services.MarketplaceClients;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using Delobytes.App.Backend.Integrations.Contracts.Models;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.Consumers;

public class ProcessOrdersImportConsumerTests
{
    private readonly Mock<IJobRunRepository> _jobRunRepo = new();
    private readonly Mock<IJobCheckpointRepository> _jobCheckpointRepo = new();
    private readonly Mock<ISyncCheckpointRepository> _syncCheckpointRepo = new();
    private readonly Mock<IChannelApiClientFactory> _clientFactory = new();
    private readonly Mock<IEventPublisher> _eventPublisher = new();
    private readonly Mock<ILogger<ProcessOrdersImportConsumer>> _logger = new();
    private readonly Mock<IOrdersChannelClient> _ordersSource = new();
    public interface IOrdersChannelClient : IChannelApiClient, IOrdersSource { }

    private ProcessOrdersImportConsumer CreateConsumer()
    {
        return new ProcessOrdersImportConsumer(
            _jobRunRepo.Object,
            _jobCheckpointRepo.Object,
            _syncCheckpointRepo.Object,
            _clientFactory.Object,
            _eventPublisher.Object,
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

    private static Connection BuildConnection(SystemChannelTemplate template, bool isActive = true)
    {
        return new Connection
        {
            Id = Guid.NewGuid(),
            ChannelId = Guid.NewGuid(),
            SystemChannelTemplateId = template.Id,
            Name = "Test WB",
            ApiKey = "test-key",
            IsActive = isActive,
            CreatedAt = DateTimeOffset.UtcNow,
            SystemChannelTemplate = template,
        };
    }

    private static JobRun BuildJobRun(Connection connection, JobStatus status = JobStatus.Pending)
    {
        return new JobRun
        {
            Id = Guid.NewGuid(),
            ConnectionId = connection.Id,
            JobType = JobType.OrdersImport,
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow,
            Connection = connection,
        };
    }

    private static ChannelOrderSnapshot BuildOrder(string externalOrderId)
    {
        return new ChannelOrderSnapshot
        {
            ExternalOrderId = externalOrderId,
            ExternalOrderNumber = externalOrderId,
            OrderDate = DateTimeOffset.UtcNow.AddDays(-1),
            StatusChangedAt = DateTimeOffset.UtcNow,
            ExternalStatus = "new",
            Currency = "RUB",
            Lines = new List<ChannelOrderLineSnapshot>()
        };
    }

    private static List<ChannelOrderSnapshot> BuildOrders(int count, int startId = 1)
    {
        List<ChannelOrderSnapshot> orders = new List<ChannelOrderSnapshot>();

        for (int i = 0; i < count; i++)
        {
            orders.Add(BuildOrder($"ORDER-{startId + i}"));
        }

        return orders;
    }

    private void SetupDefaults(JobRun jobRun)
    {
        _jobRunRepo
            .Setup(r => r.GetByIdWithConnectionAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(jobRun);

        _jobRunRepo
            .Setup(r => r.UpdateAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _jobRunRepo
            .Setup(r => r.GetOrdersImportJobAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrdersImportJob?)null);

        _jobCheckpointRepo
            .Setup(r => r.GetByJobRunIdAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((JobCheckpoint?)null);

        _jobCheckpointRepo
            .Setup(r => r.UpsertAsync(It.IsAny<JobCheckpoint>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _clientFactory
            .Setup(f => f.Create(It.IsAny<SystemChannelTemplate>()))
            .Returns(_ordersSource.Object);
    }

    [Fact]
    public async Task ProcessAsync_JobRunNotFound_ReturnsWithoutAction()
    {
        Guid jobRunId = Guid.NewGuid();

        _jobRunRepo
            .Setup(r => r.GetByIdWithConnectionAsync(jobRunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((JobRun?)null);

        ProcessOrdersImportConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(
            new OrdersImportRequestedEvent { JobRunId = jobRunId, ConnectionId = Guid.NewGuid() },
            CancellationToken.None);

        _clientFactory.Verify(f => f.Create(It.IsAny<SystemChannelTemplate>()), Times.Never);
        _eventPublisher.Verify(
            p => p.PublishAsync(It.IsAny<OrdersBatchImportRequestedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData(JobStatus.Success)]
    [InlineData(JobStatus.Failed)]
    [InlineData(JobStatus.Cancelled)]
    public async Task ProcessAsync_TerminalStatus_SkipsWithoutAction(JobStatus terminalStatus)
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        JobRun jobRun = BuildJobRun(connection, terminalStatus);

        _jobRunRepo
            .Setup(r => r.GetByIdWithConnectionAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(jobRun);

        ProcessOrdersImportConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(
            new OrdersImportRequestedEvent { JobRunId = jobRun.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        _clientFactory.Verify(f => f.Create(It.IsAny<SystemChannelTemplate>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_InactiveConnection_FailsJob()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template, isActive: false);
        JobRun jobRun = BuildJobRun(connection, JobStatus.Pending);

        SetupDefaults(jobRun);

        ProcessOrdersImportConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(
            new OrdersImportRequestedEvent { JobRunId = jobRun.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        jobRun.Status.Should().Be(JobStatus.Failed);
        jobRun.Error.Should().Contain("inactive");
        jobRun.CompletedAt.Should().NotBeNull();

        _jobRunRepo.Verify(r => r.UpdateAsync(jobRun, It.IsAny<CancellationToken>()), Times.Once);
        _clientFactory.Verify(f => f.Create(It.IsAny<SystemChannelTemplate>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_ChannelDoesNotSupportOrders_FailsJob()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        JobRun jobRun = BuildJobRun(connection, JobStatus.Pending);

        SetupDefaults(jobRun);

        Mock<IChannelApiClient> mockApiClient = new Mock<IChannelApiClient>();
        _clientFactory
            .Setup(f => f.Create(It.IsAny<SystemChannelTemplate>()))
            .Returns(mockApiClient.Object);

        ProcessOrdersImportConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(
            new OrdersImportRequestedEvent { JobRunId = jobRun.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        jobRun.Status.Should().Be(JobStatus.Failed);
        jobRun.Error.Should().Contain("does not support orders import");
        jobRun.CompletedAt.Should().NotBeNull();

        _jobRunRepo.Verify(r => r.UpdateAsync(jobRun, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ProcessAsync_FirstRun_FetchesOrdersAndPublishesBatches()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        JobRun jobRun = BuildJobRun(connection, JobStatus.Pending);
        List<ChannelOrderSnapshot> orders = BuildOrders(5);

        SetupDefaults(jobRun);

        _syncCheckpointRepo
            .Setup(r => r.GetAsync(connection.Id, JobType.OrdersImport, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncCheckpoint?)null);

        _ordersSource
            .Setup(s => s.GetOrdersAsync(connection.Id, It.IsAny<OrdersRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrdersPage { Orders = orders, HasMore = false });

        ProcessOrdersImportConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(
            new OrdersImportRequestedEvent { JobRunId = jobRun.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        _jobCheckpointRepo.Verify(
            r => r.UpsertAsync(It.IsAny<JobCheckpoint>(), It.IsAny<CancellationToken>()),
            Times.Once);

        _eventPublisher.Verify(
            p => p.PublishAsync(
                It.Is<OrdersBatchImportRequestedEvent>(e =>
                    e.JobRunId == jobRun.Id &&
                    e.Orders.Count == 5 &&
                    e.IsTerminal),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_NoOrders_PublishesEmptyBatch()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        JobRun jobRun = BuildJobRun(connection, JobStatus.Pending);

        SetupDefaults(jobRun);

        _syncCheckpointRepo
            .Setup(r => r.GetAsync(connection.Id, JobType.OrdersImport, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncCheckpoint?)null);

        _ordersSource
            .Setup(s => s.GetOrdersAsync(connection.Id, It.IsAny<OrdersRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrdersPage { Orders = new List<ChannelOrderSnapshot>(), HasMore = false });

        ProcessOrdersImportConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(
            new OrdersImportRequestedEvent { JobRunId = jobRun.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        _eventPublisher.Verify(
            p => p.PublishAsync(
                It.Is<OrdersBatchImportRequestedEvent>(e =>
                    e.JobRunId == jobRun.Id &&
                    e.Orders.Count == 0 &&
                    e.IsTerminal),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_25Orders_PublishesMultipleBatches()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        JobRun jobRun = BuildJobRun(connection, JobStatus.Pending);
        List<ChannelOrderSnapshot> orders = BuildOrders(25);

        SetupDefaults(jobRun);

        _syncCheckpointRepo
            .Setup(r => r.GetAsync(connection.Id, JobType.OrdersImport, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncCheckpoint?)null);

        _ordersSource
            .Setup(s => s.GetOrdersAsync(connection.Id, It.IsAny<OrdersRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrdersPage { Orders = orders, HasMore = false });

        ProcessOrdersImportConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(
            new OrdersImportRequestedEvent { JobRunId = jobRun.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        _eventPublisher.Verify(
            p => p.PublishAsync(It.IsAny<OrdersBatchImportRequestedEvent>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3));
    }

    [Fact]
    public async Task ProcessAsync_TransitionsPendingToRunning()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        JobRun jobRun = BuildJobRun(connection, JobStatus.Pending);

        SetupDefaults(jobRun);

        _syncCheckpointRepo
            .Setup(r => r.GetAsync(connection.Id, JobType.OrdersImport, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncCheckpoint?)null);

        _ordersSource
            .Setup(s => s.GetOrdersAsync(connection.Id, It.IsAny<OrdersRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrdersPage { Orders = new List<ChannelOrderSnapshot>(), HasMore = false });

        ProcessOrdersImportConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(
            new OrdersImportRequestedEvent { JobRunId = jobRun.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        jobRun.Status.Should().Be(JobStatus.Running);
        jobRun.StartedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ProcessAsync_DeltaSync_UsesLastChangedAtWindow()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        JobRun jobRun = BuildJobRun(connection, JobStatus.Pending);
        DateTimeOffset lastChangedAt = DateTimeOffset.UtcNow.AddHours(-2);
        OrdersRequest? capturedRequest = null;

        SetupDefaults(jobRun);

        SyncCheckpoint checkpoint = new SyncCheckpoint
        {
            Id = Guid.NewGuid(),
            ConnectionId = connection.Id,
            JobType = JobType.OrdersImport,
            LastChangedAt = lastChangedAt,
            LastSuccessAt = DateTimeOffset.UtcNow.AddHours(-2),
        };

        _syncCheckpointRepo
            .Setup(r => r.GetAsync(connection.Id, JobType.OrdersImport, It.IsAny<CancellationToken>()))
            .ReturnsAsync(checkpoint);

        _ordersSource
            .Setup(s => s.GetOrdersAsync(connection.Id, It.IsAny<OrdersRequest>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, OrdersRequest, CancellationToken>((_, req, _) => capturedRequest = req)
            .ReturnsAsync(new OrdersPage { Orders = new List<ChannelOrderSnapshot>(), HasMore = false });

        ProcessOrdersImportConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(
            new OrdersImportRequestedEvent { JobRunId = jobRun.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.Flag.Should().Be(0);
    }

    [Fact]
    public async Task CreateBatchId_IsDeterministic()
    {
        Guid jobRunId = Guid.NewGuid();

        Guid batchId1 = ProcessOrdersImportConsumer.CreateBatchId(jobRunId, 0);
        Guid batchId2 = ProcessOrdersImportConsumer.CreateBatchId(jobRunId, 0);

        batchId1.Should().Be(batchId2);
    }

    [Fact]
    public async Task CreateBatchId_DiffersByIndex()
    {
        Guid jobRunId = Guid.NewGuid();

        Guid batchId0 = ProcessOrdersImportConsumer.CreateBatchId(jobRunId, 0);
        Guid batchId1 = ProcessOrdersImportConsumer.CreateBatchId(jobRunId, 1);

        batchId0.Should().NotBe(batchId1);
    }
}
