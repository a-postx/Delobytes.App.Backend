using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Integrations.Application.Events;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Application.Options;
using Delobytes.App.Backend.Integrations.Application.Services.MarketplaceClients;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using Delobytes.App.Backend.Integrations.Contracts.Models;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers;
using Delobytes.App.Backend.Integrations.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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

    // Real instance rather than a mock: JobRunExecutionContext has no interface/virtual
    // members for Moq to override, and it is cheap enough (AsyncLocal-backed) to exercise for
    // real, the same way ProcessProductsImportConsumerTests exercises it.
    private readonly JobRunExecutionContext _jobRunExecutionContext = new();

    private ProcessOrdersImportConsumer CreateConsumer(int maxOrdersPerResponse = 80000, TimeSpan? minOrdersWindow = null)
    {
        WildberriesImportOptions options = new WildberriesImportOptions
        {
            MaxOrdersPerResponse = maxOrdersPerResponse,
            MinOrdersWindow = minOrdersWindow ?? TimeSpan.FromHours(1),
        };

        IOptions<WildberriesImportOptions> wrappedOptions = Options.Create(options);

        return new ProcessOrdersImportConsumer(
            _jobRunRepo.Object,
            _jobCheckpointRepo.Object,
            _syncCheckpointRepo.Object,
            _clientFactory.Object,
            _eventPublisher.Object,
            _jobRunExecutionContext,
            wrappedOptions,
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

    [Fact]
    public async Task ProcessAsync_SuccessPath_ClearsJobRunContext()
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
            .ReturnsAsync(new OrdersPage { Orders = BuildOrders(3), HasMore = false });

        ProcessOrdersImportConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(
            new OrdersImportRequestedEvent { JobRunId = jobRun.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        _jobRunExecutionContext.JobRunId.Should().BeNull();
    }

    [Fact]
    public async Task ProcessAsync_SourceThrows_ClearsJobRunContext()
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
            .ThrowsAsync(new HttpRequestException("wildberries unavailable"));

        ProcessOrdersImportConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(
            new OrdersImportRequestedEvent { JobRunId = jobRun.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        jobRun.Status.Should().Be(JobStatus.Failed);

        _jobRunExecutionContext.JobRunId.Should().BeNull();
    }

    [Fact]
    public async Task ProcessAsync_ChannelDoesNotSupportOrders_ClearsJobRunContext()
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

        _jobRunExecutionContext.JobRunId.Should().BeNull();
    }

    [Fact]
    public async Task ProcessAsync_JobRunContextSetBeforeSourceIsQueried()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        JobRun jobRun = BuildJobRun(connection, JobStatus.Pending);

        SetupDefaults(jobRun);

        _syncCheckpointRepo
            .Setup(r => r.GetAsync(connection.Id, JobType.OrdersImport, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncCheckpoint?)null);

        Guid? observedJobRunId = null;

        _ordersSource
            .Setup(s => s.GetOrdersAsync(connection.Id, It.IsAny<OrdersRequest>(), It.IsAny<CancellationToken>()))
            .Callback(() => observedJobRunId = _jobRunExecutionContext.JobRunId)
            .ReturnsAsync(new OrdersPage { Orders = BuildOrders(1), HasMore = false });

        ProcessOrdersImportConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(
            new OrdersImportRequestedEvent { JobRunId = jobRun.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        observedJobRunId.Should().Be(jobRun.Id);
    }

    [Fact]
    public async Task ProcessAsync_FirstResponseIsPartial_BisectsWindowAndMergesBothHalves()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        JobRun jobRun = BuildJobRun(connection, JobStatus.Pending);

        SetupDefaults(jobRun);

        _syncCheckpointRepo
            .Setup(r => r.GetAsync(connection.Id, JobType.OrdersImport, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncCheckpoint?)null);

        int call = 0;

        _ordersSource
            .Setup(s => s.GetOrdersAsync(connection.Id, It.IsAny<OrdersRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                call++;

                if (call == 1)
                {
                    return new OrdersPage { Orders = BuildOrders(3, 1), HasMore = true };
                }

                if (call == 2)
                {
                    return new OrdersPage { Orders = BuildOrders(2, 1), HasMore = false };
                }

                return new OrdersPage { Orders = BuildOrders(2, 3), HasMore = false };
            });

        ProcessOrdersImportConsumer consumer = CreateConsumer(maxOrdersPerResponse: 3);

        await consumer.ProcessAsync(
            new OrdersImportRequestedEvent { JobRunId = jobRun.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        _ordersSource.Verify(
            s => s.GetOrdersAsync(connection.Id, It.IsAny<OrdersRequest>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3));

        _eventPublisher.Verify(
            p => p.PublishAsync(
                It.Is<OrdersBatchImportRequestedEvent>(e => e.IsTerminal),
                It.IsAny<CancellationToken>()),
            Times.Once);

        List<string> publishedOrderIds = new List<string>();

        foreach (OrdersBatchImportRequestedEvent published in CapturedBatches())
        {
            publishedOrderIds.AddRange(published.Orders.Select(o => o.ExternalOrderId));
        }

        publishedOrderIds.Should().BeEquivalentTo(new[] { "ORDER-1", "ORDER-2", "ORDER-3", "ORDER-4" });
    }

    [Fact]
    public async Task ProcessAsync_OrderPresentInBothHalves_IsPublishedOnce()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        JobRun jobRun = BuildJobRun(connection, JobStatus.Pending);

        SetupDefaults(jobRun);

        _syncCheckpointRepo
            .Setup(r => r.GetAsync(connection.Id, JobType.OrdersImport, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncCheckpoint?)null);

        int call = 0;

        _ordersSource
            .Setup(s => s.GetOrdersAsync(connection.Id, It.IsAny<OrdersRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                call++;

                if (call == 1)
                {
                    return new OrdersPage { Orders = BuildOrders(3, 1), HasMore = true };
                }

                if (call == 2)
                {
                    return new OrdersPage { Orders = BuildOrders(2, 1), HasMore = false };
                }

                return new OrdersPage { Orders = BuildOrders(2, 2), HasMore = false };
            });

        ProcessOrdersImportConsumer consumer = CreateConsumer(maxOrdersPerResponse: 3);

        await consumer.ProcessAsync(
            new OrdersImportRequestedEvent { JobRunId = jobRun.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        List<string> publishedOrderIds = new List<string>();

        foreach (OrdersBatchImportRequestedEvent published in CapturedBatches())
        {
            publishedOrderIds.AddRange(published.Orders.Select(o => o.ExternalOrderId));
        }

        publishedOrderIds.Should().BeEquivalentTo(new[] { "ORDER-1", "ORDER-2", "ORDER-3" });
        publishedOrderIds.Should().HaveCount(3);
    }

    [Fact]
    public async Task ProcessAsync_PartialResponseAtMinimumWindow_FailsTheRun()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        JobRun jobRun = BuildJobRun(connection, JobStatus.Pending);

        SetupDefaults(jobRun);

        _syncCheckpointRepo
            .Setup(r => r.GetAsync(connection.Id, JobType.OrdersImport, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncCheckpoint
            {
                Id = Guid.NewGuid(),
                ConnectionId = connection.Id,
                JobType = JobType.OrdersImport,
                LastChangedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            });

        _ordersSource
            .Setup(s => s.GetOrdersAsync(connection.Id, It.IsAny<OrdersRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrdersPage { Orders = BuildOrders(3), HasMore = true });

        ProcessOrdersImportConsumer consumer = CreateConsumer(maxOrdersPerResponse: 3, minOrdersWindow: TimeSpan.FromMinutes(30));

        await consumer.ProcessAsync(
            new OrdersImportRequestedEvent { JobRunId = jobRun.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        List<OrdersRequest> issuedRequests = new List<OrdersRequest>();

        _ordersSource
            .Invocations
            .ToList()
            .ForEach(i => issuedRequests.Add((OrdersRequest)i.Arguments[1]));

        // The window is the 10 minutes since LastChangedAt plus the 30-minute safety overlap,
        // i.e. 40 minutes: one request for the whole window, then one for its first half, which
        // is already only 20 minutes wide and therefore at the floor. The floor stops the
        // recursion there instead of halving it again into two 10-minute windows.
        _ordersSource.Verify(
            s => s.GetOrdersAsync(connection.Id, It.IsAny<OrdersRequest>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));

        issuedRequests.Should().HaveCount(2);
        (issuedRequests[0].DateTo - issuedRequests[0].DateFrom).Should().BeGreaterThan(TimeSpan.FromMinutes(30));
        (issuedRequests[1].DateTo - issuedRequests[1].DateFrom).Should().BeLessThanOrEqualTo(TimeSpan.FromMinutes(30));

        jobRun.Status.Should().Be(JobStatus.Failed);

        _eventPublisher.Verify(
            p => p.PublishAsync(It.IsAny<OrdersBatchImportRequestedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);

        _jobRunExecutionContext.JobRunId.Should().BeNull();
    }

    [Fact]
    public async Task ProcessAsync_BackfillOrdersExceedOldHardcodedPageLimit_PublishesEveryOrder()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        JobRun jobRun = BuildJobRun(connection, JobStatus.Pending);

        // 150 exceeds the old hardcoded PageLimit (100) that BUG-1 silently truncated results to.
        // A single response under the configured cap, HasMore = false, is enough to prove nothing
        // client-side truncates it anymore.
        List<ChannelOrderSnapshot> orders = BuildOrders(150);

        SetupDefaults(jobRun);

        _syncCheckpointRepo
            .Setup(r => r.GetAsync(connection.Id, JobType.OrdersImport, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncCheckpoint?)null);

        OrdersRequest? issuedRequest = null;

        _ordersSource
            .Setup(s => s.GetOrdersAsync(connection.Id, It.IsAny<OrdersRequest>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, OrdersRequest, CancellationToken>((_, request, _) => issuedRequest = request)
            .ReturnsAsync(new OrdersPage { Orders = orders, HasMore = false });

        ProcessOrdersImportConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(
            new OrdersImportRequestedEvent { JobRunId = jobRun.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        List<string> publishedOrderIds = new List<string>();

        foreach (OrdersBatchImportRequestedEvent published in CapturedBatches())
        {
            publishedOrderIds.AddRange(published.Orders.Select(o => o.ExternalOrderId));
        }

        publishedOrderIds.Should().HaveCount(150);
        publishedOrderIds.Distinct().Should().HaveCount(150, "every order must be published exactly once");

        // The old PageLimit field is gone; nothing on this path may ask the source to cap itself.
        issuedRequest.Should().NotBeNull();
        issuedRequest!.Limit.Should().Be(0);
    }

    private List<OrdersBatchImportRequestedEvent> CapturedBatches()
    {
        List<OrdersBatchImportRequestedEvent> captured = new List<OrdersBatchImportRequestedEvent>();

        _eventPublisher
            .Invocations
            .Where(i => i.Arguments.Count > 0 && i.Arguments[0] is OrdersBatchImportRequestedEvent)
            .ToList()
            .ForEach(i => captured.Add((OrdersBatchImportRequestedEvent)i.Arguments[0]));

        return captured;
    }
}
