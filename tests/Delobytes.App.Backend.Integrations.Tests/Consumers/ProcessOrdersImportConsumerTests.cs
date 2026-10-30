using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Integrations.Application.Events;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Application.Options;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using Delobytes.App.Backend.Integrations.Contracts.Models;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers;
using Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers.OrdersImport;
using Delobytes.App.Backend.Integrations.Infrastructure.Services;
using Delobytes.App.Backend.Integrations.Tests.ApiClients;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.Consumers;

public partial class ProcessOrdersImportConsumerTests
{
    private readonly FakeTimeProvider _timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);

    private readonly Mock<IJobRunRepository> _jobRunRepo = new();
    private readonly Mock<IJobCheckpointRepository> _jobCheckpointRepo = new();
    private readonly Mock<ISyncCheckpointRepository> _syncCheckpointRepo = new();
    private readonly Mock<IOrdersBackfillStateRepository> _backfillStateRepo = new();
    private readonly Mock<IChannelApiClientFactory> _clientFactory = new();
    private readonly Mock<IEventPublisher> _eventPublisher = new();
    private readonly Mock<ILogger<ProcessOrdersImportConsumer>> _logger = new();
    private readonly Mock<ILogger<DeltaOrdersFetcher>> _deltaFetcherLogger = new();
    private readonly Mock<IOrdersChannelClient> _ordersSource = new();
    public interface IOrdersChannelClient : IChannelApiClient, IOrdersSource { }

    // Real instance rather than a mock: JobRunExecutionContext has no interface/virtual
    // members for Moq to override, and it is cheap enough (AsyncLocal-backed) to exercise for
    // real, the same way ProcessProductsImportConsumerTests exercises it.
    private readonly JobRunExecutionContext _jobRunExecutionContext = new();

    private ProcessOrdersImportConsumer CreateConsumer(
        int maxOrdersPerResponse = 80000,
        TimeSpan? minOrdersWindow = null,
        IWildberriesRateLimiter? rateLimiter = null)
    {
        WildberriesImportOptions options = new WildberriesImportOptions
        {
            MaxOrdersPerResponse = maxOrdersPerResponse,
            MinOrdersWindow = minOrdersWindow ?? TimeSpan.FromHours(1),
        };

        IOptions<WildberriesImportOptions> wrappedOptions = Options.Create(options);

        OrdersImportCheckpointStore checkpointStore = new OrdersImportCheckpointStore(
            _jobCheckpointRepo.Object,
            _timeProvider);

        OrdersBatchPublisher batchPublisher = new OrdersBatchPublisher(
            _eventPublisher.Object,
            checkpointStore);

        IWildberriesRateLimiter effectiveRateLimiter = rateLimiter ?? new AlwaysAllowWildberriesRateLimiter();

        PacedOrdersSource pacedSource = new PacedOrdersSource(
            effectiveRateLimiter,
            _timeProvider,
            wrappedOptions);

        BisectingOrdersReader bisectingReader = new BisectingOrdersReader(
            pacedSource,
            new Mock<ILogger<BisectingOrdersReader>>().Object,
            wrappedOptions);

        OrdersBackfillWalker backfillWalker = new OrdersBackfillWalker(
            pacedSource,
            _backfillStateRepo.Object,
            batchPublisher,
            new Mock<ILogger<OrdersBackfillWalker>>().Object,
            _timeProvider);

        return new ProcessOrdersImportConsumer(
            _jobRunRepo.Object,
            checkpointStore,
            backfillWalker,
            new BackfillOrdersFetcher(backfillWalker),
            new DeltaOrdersFetcher(
                bisectingReader,
                _syncCheckpointRepo.Object,
                batchPublisher,
                _deltaFetcherLogger.Object),
            _clientFactory.Object,
            _eventPublisher.Object,
            _jobRunExecutionContext,
            wrappedOptions,
            _logger.Object,
            _timeProvider,
            new JobRunLifecycle(_jobRunRepo.Object, _timeProvider),
            new OrdersContinuationScheduler(_jobRunRepo.Object, _timeProvider));
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
            Lines = new List<ChannelOrderLineSnapshot> { BuildLine(externalOrderId) }
        };
    }

    private static ChannelOrderLineSnapshot BuildLine(
        string lineKey,
        bool isCancelled = false,
        DateTimeOffset? statusChangedAt = null)
    {
        return new ChannelOrderLineSnapshot
        {
            LineKey = lineKey,
            ExternalLineId = lineKey,
            ExternalProductId = $"nm-{lineKey}",
            OfferId = $"SKU-{lineKey}",
            ProductName = "Test Product",
            Quantity = 1,
            UnitPrice = 1000m,
            BuyerUnitPrice = 950m,
            UnitPriceBeforeDiscount = 1200m,
            Currency = "RUB",
            IsCancelled = isCancelled,
            StatusChangedAt = statusChangedAt,
            ChannelReportedTotal = 1200m,
        };
    }

    /// <summary>
    /// Builds a cart snapshot: one order identity carrying one line per ordered unit. A cart has no
    /// cart-level total in the channel payload, so ChannelReportedTotal stays null on it.
    /// </summary>
    private static ChannelOrderSnapshot BuildCart(string externalOrderId, params string[] lineKeys)
    {
        List<ChannelOrderLineSnapshot> lines = new List<ChannelOrderLineSnapshot>();

        foreach (string lineKey in lineKeys)
        {
            lines.Add(BuildLine(lineKey, statusChangedAt: DateTimeOffset.UtcNow));
        }

        return new ChannelOrderSnapshot
        {
            ExternalOrderId = externalOrderId,
            ExternalOrderNumber = externalOrderId,
            OrderDate = DateTimeOffset.UtcNow.AddDays(-1),
            StatusChangedAt = DateTimeOffset.UtcNow,
            ExternalStatus = "new",
            Currency = "RUB",
            ChannelReportedTotal = lines.Count == 1 ? 1200m : null,
            Lines = lines,
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

        _jobCheckpointRepo
            .Setup(r => r.GetByJobRunIdAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((JobCheckpoint?)null);

        _jobCheckpointRepo
            .Setup(r => r.UpsertAsync(It.IsAny<JobCheckpoint>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _backfillStateRepo
            .Setup(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrdersBackfillState
            {
                Id = Guid.NewGuid(),
                ConnectionId = Guid.Empty,
                NextDayToFetch = DateTimeOffset.UtcNow.AddDays(-90),
                OldestDayInScope = _timeProvider.GetUtcNow().AddDays(-90),
                IsCompleted = true,
                CompletedAt = DateTimeOffset.UtcNow,
                HandoverWatermark = _timeProvider.GetUtcNow().AddDays(-90),
            });

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

        // Two writes: the progress payload persisted right after the unit's batches are published
        // (IsFetchCompleted = false), then the final payload that announces fetch completion.
        _jobCheckpointRepo.Verify(
            r => r.UpsertAsync(It.IsAny<JobCheckpoint>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3));

        _eventPublisher.Verify(
            p => p.PublishAsync(
                It.Is<OrdersBatchImportRequestedEvent>(e =>
                    e.JobRunId == jobRun.Id &&
                    e.Orders.Count == 5),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _eventPublisher.Verify(
            p => p.PublishAsync(
                It.Is<OrdersImportFetchCompletedEvent>(e =>
                    e.JobRunId == jobRun.Id &&
                    e.TotalBatches == 1),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_NoOrders_PublishesFetchCompletedWithNoBatches()
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
            p => p.PublishAsync(It.IsAny<OrdersBatchImportRequestedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);

        _eventPublisher.Verify(
            p => p.PublishAsync(
                It.Is<OrdersImportFetchCompletedEvent>(e =>
                    e.JobRunId == jobRun.Id &&
                    e.TotalBatches == 0),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_75Orders_PublishesMultipleBatches()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        JobRun jobRun = BuildJobRun(connection, JobStatus.Pending);
        List<ChannelOrderSnapshot> orders = BuildOrders(75);

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

        // 75 orders at a batch size of 30 make exactly three data batches; the separate
        // OrdersImportFetchCompletedEvent carries the threshold instead of an empty terminal batch.
        _eventPublisher.Verify(
            p => p.PublishAsync(It.IsAny<OrdersBatchImportRequestedEvent>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3));

        _eventPublisher.Verify(
            p => p.PublishAsync(
                It.Is<OrdersImportFetchCompletedEvent>(e =>
                    e.JobRunId == jobRun.Id &&
                    e.TotalBatches == 3),
                It.IsAny<CancellationToken>()),
            Times.Once);
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
        capturedRequest.DateFrom.Should().Be((lastChangedAt - OrdersImportConstants.SafetyOverlap)
            .ToOffset(TimeSpan.FromHours(3)));
    }

    [Fact]
    public async Task ProcessAsync_LastChangeDateBeyondWindowTo_ClampsWatermarkToWindowTo()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        JobRun jobRun = BuildJobRun(connection, JobStatus.Pending);
        OrdersImportCheckpointPayload? persistedPayload = null;

        SetupDefaults(jobRun);
        CapturePersistedPayload(payload => persistedPayload = payload);

        _syncCheckpointRepo
            .Setup(r => r.GetAsync(connection.Id, JobType.OrdersImport, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncCheckpoint
            {
                Id = Guid.NewGuid(),
                ConnectionId = connection.Id,
                JobType = JobType.OrdersImport,
                LastChangedAt = DateTimeOffset.UtcNow.AddHours(-2),
            });

        DateTimeOffset bogusLastChangeDate = DateTimeOffset.UtcNow.AddDays(3650);

        _ordersSource
            .Setup(s => s.GetOrdersAsync(connection.Id, It.IsAny<OrdersRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrdersPage
            {
                Orders = new List<ChannelOrderSnapshot>
                {
                    BuildOrder("ORDER-1") with { StatusChangedAt = bogusLastChangeDate },
                },
                HasMore = false,
            });

        ProcessOrdersImportConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(
            new OrdersImportRequestedEvent { JobRunId = jobRun.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        persistedPayload.Should().NotBeNull();
        persistedPayload!.WindowTo.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));

        // The only defensible watermark is the end of the window the endpoint actually served.
        persistedPayload.MaxChangedAt.Should().Be(persistedPayload.WindowTo);
        persistedPayload.MaxChangedAt.Should().BeBefore(bogusLastChangeDate);
    }

    [Fact]
    public async Task ProcessAsync_NoLastChangeDate_DoesNotDeriveWatermarkFromOrderDate()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        JobRun jobRun = BuildJobRun(connection, JobStatus.Pending);
        OrdersImportCheckpointPayload? persistedPayload = null;

        SetupDefaults(jobRun);
        CapturePersistedPayload(payload => persistedPayload = payload);

        _syncCheckpointRepo
            .Setup(r => r.GetAsync(connection.Id, JobType.OrdersImport, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncCheckpoint
            {
                Id = Guid.NewGuid(),
                ConnectionId = connection.Id,
                JobType = JobType.OrdersImport,
                LastChangedAt = DateTimeOffset.UtcNow.AddHours(-2),
            });

        _ordersSource
            .Setup(s => s.GetOrdersAsync(connection.Id, It.IsAny<OrdersRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrdersPage
            {
                Orders = new List<ChannelOrderSnapshot>
                {
                    BuildOrder("ORDER-1") with
                    {
                        StatusChangedAt = null,
                        OrderDate = DateTimeOffset.UtcNow.AddDays(30),
                    },
                },
                HasMore = false,
            });

        ProcessOrdersImportConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(
            new OrdersImportRequestedEvent { JobRunId = jobRun.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        persistedPayload.Should().NotBeNull();

        // null keeps the aggregator's "do not advance the watermark" meaning intact; OrderDate
        // sits on a different time axis than the one the delta endpoint selects on.
        persistedPayload!.MaxChangedAt.Should().BeNull();
    }

    [Fact]
    public async Task ProcessAsync_LastChangeDateInsideWindow_WatermarkEqualsObservedValue()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        JobRun jobRun = BuildJobRun(connection, JobStatus.Pending);
        OrdersImportCheckpointPayload? persistedPayload = null;

        SetupDefaults(jobRun);
        CapturePersistedPayload(payload => persistedPayload = payload);

        _syncCheckpointRepo
            .Setup(r => r.GetAsync(connection.Id, JobType.OrdersImport, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncCheckpoint
            {
                Id = Guid.NewGuid(),
                ConnectionId = connection.Id,
                JobType = JobType.OrdersImport,
                LastChangedAt = DateTimeOffset.UtcNow.AddHours(-2),
            });

        DateTimeOffset observedLastChangeDate = DateTimeOffset.UtcNow.AddMinutes(-30);

        _ordersSource
            .Setup(s => s.GetOrdersAsync(connection.Id, It.IsAny<OrdersRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrdersPage
            {
                Orders = new List<ChannelOrderSnapshot>
                {
                    BuildOrder("ORDER-1") with { StatusChangedAt = observedLastChangeDate },
                    BuildOrder("ORDER-2") with { StatusChangedAt = observedLastChangeDate.AddMinutes(-10) },
                },
                HasMore = false,
            });

        ProcessOrdersImportConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(
            new OrdersImportRequestedEvent { JobRunId = jobRun.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        persistedPayload.Should().NotBeNull();
        persistedPayload!.MaxChangedAt.Should().Be(observedLastChangeDate.ToUniversalTime());

        WarningMessages().Should().BeEmpty();
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
                It.IsAny<OrdersImportFetchCompletedEvent>(),
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
    public async Task ProcessAsync_CartSplitAcrossBothHalves_PublishesOneOrderWithEveryLine()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        JobRun jobRun = BuildJobRun(connection, JobStatus.Pending);

        SetupDefaults(jobRun);

        _syncCheckpointRepo
            .Setup(r => r.GetAsync(connection.Id, JobType.OrdersImport, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncCheckpoint?)null);

        // Both halves report the same cart but a different unit of it, which is what a bisection
        // through a cart produces. The first half carries the older header, so a merge that let the
        // newer report's Lines replace the accumulated set would publish one line instead of two.
        DateTimeOffset earlier = DateTimeOffset.UtcNow.AddMinutes(-30);
        DateTimeOffset later = DateTimeOffset.UtcNow.AddMinutes(-5);

        int call = 0;

        _ordersSource
            .Setup(s => s.GetOrdersAsync(connection.Id, It.IsAny<OrdersRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                call++;

                if (call == 1)
                {
                    return new OrdersPage { Orders = BuildOrders(3, 10), HasMore = true };
                }

                if (call == 2)
                {
                    return new OrdersPage
                    {
                        Orders = new List<ChannelOrderSnapshot>
                        {
                            BuildCart("ORDER-1", "SRID-1") with
                            {
                                StatusChangedAt = earlier,
                                Lines = new List<ChannelOrderLineSnapshot>
                                {
                                    BuildLine("SRID-1", statusChangedAt: earlier),
                                },
                            },
                        },
                        HasMore = false,
                    };
                }

                return new OrdersPage
                {
                    Orders = new List<ChannelOrderSnapshot>
                    {
                        BuildCart("ORDER-1", "SRID-2") with
                        {
                            StatusChangedAt = later,
                            Lines = new List<ChannelOrderLineSnapshot>
                            {
                                BuildLine("SRID-2", statusChangedAt: later),
                            },
                        },
                    },
                    HasMore = false,
                };
            });

        ProcessOrdersImportConsumer consumer = CreateConsumer(maxOrdersPerResponse: 1);

        await consumer.ProcessAsync(
            new OrdersImportRequestedEvent { JobRunId = jobRun.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        List<ChannelOrderSnapshot> cart = PublishedOrders().Where(o => o.ExternalOrderId == "ORDER-1").ToList();

        // One order, not two: both halves named the same cart.
        cart.Should().ContainSingle();
        cart.Single().Lines.Select(l => l.LineKey).Should().BeEquivalentTo(new[] { "SRID-1", "SRID-2" });

        // The header instant is the newest per-unit instant in the merged line set.
        cart.Single().StatusChangedAt.Should().Be(later);
    }

    [Fact]
    public async Task ProcessAsync_LiveUnitOnEarlierReportAndCancelledUnitOnLaterReport_StaysActive()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        JobRun jobRun = BuildJobRun(connection, JobStatus.Pending);

        SetupDefaults(jobRun);

        _syncCheckpointRepo
            .Setup(r => r.GetAsync(connection.Id, JobType.OrdersImport, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncCheckpoint?)null);

        DateTimeOffset earlier = DateTimeOffset.UtcNow.AddMinutes(-30);
        DateTimeOffset later = DateTimeOffset.UtcNow.AddMinutes(-5);

        int call = 0;

        _ordersSource
            .Setup(s => s.GetOrdersAsync(connection.Id, It.IsAny<OrdersRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                call++;

                if (call == 1)
                {
                    return new OrdersPage { Orders = BuildOrders(1, 10), HasMore = true };
                }

                if (call == 2)
                {
                    // The earlier report holds the only live unit and describes its own cart as active.
                    return new OrdersPage
                    {
                        Orders = new List<ChannelOrderSnapshot>
                        {
                            BuildCart("ORDER-1", "SRID-1") with
                            {
                                StatusChangedAt = earlier,
                                Lines = new List<ChannelOrderLineSnapshot>
                                {
                                    BuildLine("SRID-1", isCancelled: false, statusChangedAt: earlier),
                                },
                            },
                        },
                        HasMore = false,
                    };
                }

                // The later report holds the only cancelled unit. Taking its header, as the previous
                // merge did, marked the whole cart cancelled while a live unit sat in it.
                return new OrdersPage
                {
                    Orders = new List<ChannelOrderSnapshot>
                    {
                        BuildCart("ORDER-1", "SRID-2") with
                        {
                            StatusChangedAt = later,
                            ExternalStatus = "cancelled",
                            CancelReason = "cancel",
                            Lines = new List<ChannelOrderLineSnapshot>
                            {
                                BuildLine("SRID-2", isCancelled: true, statusChangedAt: later),
                            },
                        },
                    },
                    HasMore = false,
                };
            });

        ProcessOrdersImportConsumer consumer = CreateConsumer(maxOrdersPerResponse: 1);

        await consumer.ProcessAsync(
            new OrdersImportRequestedEvent { JobRunId = jobRun.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        ChannelOrderSnapshot cart = PublishedOrders().Single(o => o.ExternalOrderId == "ORDER-1");

        cart.Lines.Select(l => l.LineKey).Should().BeEquivalentTo(new[] { "SRID-1", "SRID-2" });
        cart.ExternalStatus.Should().Be("active");
        cart.CancelReason.Should().BeNull();
        cart.StatusChangedAt.Should().Be(later);
    }

    [Fact]
    public async Task ProcessAsync_CancelledUnitOnEarlierReportAndLiveUnitOnLaterReport_StaysActive()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        JobRun jobRun = BuildJobRun(connection, JobStatus.Pending);

        SetupDefaults(jobRun);

        _syncCheckpointRepo
            .Setup(r => r.GetAsync(connection.Id, JobType.OrdersImport, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncCheckpoint?)null);

        DateTimeOffset earlier = DateTimeOffset.UtcNow.AddMinutes(-30);
        DateTimeOffset later = DateTimeOffset.UtcNow.AddMinutes(-5);

        int call = 0;

        _ordersSource
            .Setup(s => s.GetOrdersAsync(connection.Id, It.IsAny<OrdersRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                call++;

                if (call == 1)
                {
                    return new OrdersPage { Orders = BuildOrders(1, 10), HasMore = true };
                }

                if (call == 2)
                {
                    return new OrdersPage
                    {
                        Orders = new List<ChannelOrderSnapshot>
                        {
                            BuildCart("ORDER-1", "SRID-1") with
                            {
                                StatusChangedAt = earlier,
                                ExternalStatus = "cancelled",
                                CancelReason = "cancel",
                                Lines = new List<ChannelOrderLineSnapshot>
                                {
                                    BuildLine("SRID-1", isCancelled: true, statusChangedAt: earlier),
                                },
                            },
                        },
                        HasMore = false,
                    };
                }

                // The live unit arrives last. The cart still holds a cancelled unit, so it is not
                // cancelled; the status follows the line set, not whichever report won.
                return new OrdersPage
                {
                    Orders = new List<ChannelOrderSnapshot>
                    {
                        BuildCart("ORDER-1", "SRID-2") with
                        {
                            StatusChangedAt = later,
                            Lines = new List<ChannelOrderLineSnapshot>
                            {
                                BuildLine("SRID-2", isCancelled: false, statusChangedAt: later),
                            },
                        },
                    },
                    HasMore = false,
                };
            });

        ProcessOrdersImportConsumer consumer = CreateConsumer(maxOrdersPerResponse: 1);

        await consumer.ProcessAsync(
            new OrdersImportRequestedEvent { JobRunId = jobRun.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        ChannelOrderSnapshot cart = PublishedOrders().Single(o => o.ExternalOrderId == "ORDER-1");

        cart.Lines.Select(l => l.LineKey).Should().BeEquivalentTo(new[] { "SRID-1", "SRID-2" });
        cart.ExternalStatus.Should().Be("active");
        cart.CancelReason.Should().BeNull();
    }

    [Fact]
    public async Task ProcessAsync_EveryUnitCancelledAcrossBothHalves_IsCancelled()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        JobRun jobRun = BuildJobRun(connection, JobStatus.Pending);

        SetupDefaults(jobRun);

        _syncCheckpointRepo
            .Setup(r => r.GetAsync(connection.Id, JobType.OrdersImport, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncCheckpoint?)null);

        DateTimeOffset earlier = DateTimeOffset.UtcNow.AddMinutes(-30);
        DateTimeOffset later = DateTimeOffset.UtcNow.AddMinutes(-5);

        int call = 0;

        _ordersSource
            .Setup(s => s.GetOrdersAsync(connection.Id, It.IsAny<OrdersRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                call++;

                if (call == 1)
                {
                    return new OrdersPage { Orders = BuildOrders(1, 10), HasMore = true };
                }

                if (call == 2)
                {
                    return new OrdersPage
                    {
                        Orders = new List<ChannelOrderSnapshot>
                        {
                            BuildCart("ORDER-1", "SRID-1") with
                            {
                                StatusChangedAt = earlier,
                                Lines = new List<ChannelOrderLineSnapshot>
                                {
                                    BuildLine("SRID-1", isCancelled: true, statusChangedAt: earlier),
                                },
                            },
                        },
                        HasMore = false,
                    };
                }

                return new OrdersPage
                {
                    Orders = new List<ChannelOrderSnapshot>
                    {
                        BuildCart("ORDER-1", "SRID-2") with
                        {
                            StatusChangedAt = later,
                            Lines = new List<ChannelOrderLineSnapshot>
                            {
                                BuildLine("SRID-2", isCancelled: true, statusChangedAt: later),
                            },
                        },
                    },
                    HasMore = false,
                };
            });

        ProcessOrdersImportConsumer consumer = CreateConsumer(maxOrdersPerResponse: 1);

        await consumer.ProcessAsync(
            new OrdersImportRequestedEvent { JobRunId = jobRun.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        ChannelOrderSnapshot cart = PublishedOrders().Single(o => o.ExternalOrderId == "ORDER-1");

        cart.ExternalStatus.Should().Be("cancelled");
        cart.CancelReason.Should().Be("cancel");
    }

    [Fact]
    public async Task ProcessAsync_CartSplitAcrossBothHalves_ReportsNoChannelTotal()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        JobRun jobRun = BuildJobRun(connection, JobStatus.Pending);

        SetupDefaults(jobRun);

        _syncCheckpointRepo
            .Setup(r => r.GetAsync(connection.Id, JobType.OrdersImport, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncCheckpoint?)null);

        DateTimeOffset earlier = DateTimeOffset.UtcNow.AddMinutes(-30);
        DateTimeOffset later = DateTimeOffset.UtcNow.AddMinutes(-5);

        int call = 0;

        _ordersSource
            .Setup(s => s.GetOrdersAsync(connection.Id, It.IsAny<OrdersRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                call++;

                if (call == 1)
                {
                    return new OrdersPage { Orders = BuildOrders(1, 10), HasMore = true };
                }

                // Each half sees one unit, so each is entitled to report that unit's amount as its
                // own cart total. The merged cart holds two units and has no cart-level total, so
                // keeping either half's value would report one unit's price as the price of both.
                if (call == 2)
                {
                    return new OrdersPage
                    {
                        Orders = new List<ChannelOrderSnapshot>
                        {
                            BuildCart("ORDER-1", "SRID-1") with
                            {
                                StatusChangedAt = earlier,
                                ChannelReportedTotal = 1200m,
                            },
                        },
                        HasMore = false,
                    };
                }

                return new OrdersPage
                {
                    Orders = new List<ChannelOrderSnapshot>
                    {
                        BuildCart("ORDER-1", "SRID-2") with
                        {
                            StatusChangedAt = later,
                            ChannelReportedTotal = 1200m,
                        },
                    },
                    HasMore = false,
                };
            });

        ProcessOrdersImportConsumer consumer = CreateConsumer(maxOrdersPerResponse: 1);

        await consumer.ProcessAsync(
            new OrdersImportRequestedEvent { JobRunId = jobRun.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        ChannelOrderSnapshot cart = PublishedOrders().Single(o => o.ExternalOrderId == "ORDER-1");

        cart.Lines.Should().HaveCount(2);
        cart.ChannelReportedTotal.Should().BeNull();
    }

    [Fact]
    public async Task ProcessAsync_CartSplitAcrossBothHalves_KeepsTheEarliestOrderDate()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        JobRun jobRun = BuildJobRun(connection, JobStatus.Pending);

        SetupDefaults(jobRun);

        _syncCheckpointRepo
            .Setup(r => r.GetAsync(connection.Id, JobType.OrdersImport, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncCheckpoint?)null);

        DateTimeOffset earliest = DateTimeOffset.UtcNow.AddDays(-2);
        DateTimeOffset laterPlacement = DateTimeOffset.UtcNow.AddDays(-1);
        DateTimeOffset later = DateTimeOffset.UtcNow.AddMinutes(-5);

        int call = 0;

        _ordersSource
            .Setup(s => s.GetOrdersAsync(connection.Id, It.IsAny<OrdersRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                call++;

                if (call == 1)
                {
                    return new OrdersPage { Orders = BuildOrders(1, 10), HasMore = true };
                }

                if (call == 2)
                {
                    return new OrdersPage
                    {
                        Orders = new List<ChannelOrderSnapshot>
                        {
                            BuildCart("ORDER-1", "SRID-1") with { OrderDate = earliest, StatusChangedAt = later },
                        },
                        HasMore = false,
                    };
                }

                // The later report's own placement instant is newer than the cart's earliest unit, so
                // taking it wholesale would move the whole cart forward in time.
                return new OrdersPage
                {
                    Orders = new List<ChannelOrderSnapshot>
                    {
                        BuildCart("ORDER-1", "SRID-2") with { OrderDate = laterPlacement, StatusChangedAt = later },
                    },
                    HasMore = false,
                };
            });

        ProcessOrdersImportConsumer consumer = CreateConsumer(maxOrdersPerResponse: 1);

        await consumer.ProcessAsync(
            new OrdersImportRequestedEvent { JobRunId = jobRun.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        PublishedOrders().Single(o => o.ExternalOrderId == "ORDER-1").OrderDate.Should().Be(earliest);
    }

    [Fact]
    public async Task ProcessAsync_CollidingLine_KeepsTheVersionWithTheLaterLineInstant()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        JobRun jobRun = BuildJobRun(connection, JobStatus.Pending);

        SetupDefaults(jobRun);

        _syncCheckpointRepo
            .Setup(r => r.GetAsync(connection.Id, JobType.OrdersImport, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncCheckpoint?)null);

        DateTimeOffset oldLine = DateTimeOffset.UtcNow.AddHours(-4);
        DateTimeOffset newLine = DateTimeOffset.UtcNow.AddHours(-1);

        // The order-level comparison points the opposite way from the line-level one: the later
        // report carries the older version of the colliding line. A single flag derived from the
        // report would install the stale line version.
        DateTimeOffset earlierReport = DateTimeOffset.UtcNow.AddMinutes(-40);
        DateTimeOffset laterReport = DateTimeOffset.UtcNow.AddMinutes(-2);

        int call = 0;

        _ordersSource
            .Setup(s => s.GetOrdersAsync(connection.Id, It.IsAny<OrdersRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                call++;

                if (call == 1)
                {
                    return new OrdersPage { Orders = BuildOrders(1, 10), HasMore = true };
                }

                if (call == 2)
                {
                    return new OrdersPage
                    {
                        Orders = new List<ChannelOrderSnapshot>
                        {
                            BuildCart("ORDER-1", "SRID-1") with
                            {
                                StatusChangedAt = earlierReport,
                                Lines = new List<ChannelOrderLineSnapshot>
                                {
                                    BuildLine("SRID-1", isCancelled: true, statusChangedAt: newLine),
                                },
                            },
                        },
                        HasMore = false,
                    };
                }

                return new OrdersPage
                {
                    Orders = new List<ChannelOrderSnapshot>
                    {
                        BuildCart("ORDER-1", "SRID-1") with
                        {
                            StatusChangedAt = laterReport,
                            Lines = new List<ChannelOrderLineSnapshot>
                            {
                                BuildLine("SRID-1", isCancelled: false, statusChangedAt: oldLine),
                            },
                        },
                    },
                    HasMore = false,
                };
            });

        ProcessOrdersImportConsumer consumer = CreateConsumer(maxOrdersPerResponse: 1);

        await consumer.ProcessAsync(
            new OrdersImportRequestedEvent { JobRunId = jobRun.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        ChannelOrderSnapshot cart = PublishedOrders().Single(o => o.ExternalOrderId == "ORDER-1");

        cart.Lines.Should().ContainSingle();
        cart.Lines.Single().StatusChangedAt.Should().Be(newLine);
        cart.Lines.Single().IsCancelled.Should().BeTrue();
        cart.ExternalStatus.Should().Be("cancelled");
    }

    [Fact]
    public async Task ProcessAsync_SameCartRefetched_PublishesTheSameLinesWithoutDuplicating()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        JobRun firstJobRun = BuildJobRun(connection, JobStatus.Pending);
        JobRun secondJobRun = BuildJobRun(connection, JobStatus.Pending);

        SetupDefaults(firstJobRun);
        SetupDefaults(secondJobRun);

        _syncCheckpointRepo
            .Setup(r => r.GetAsync(connection.Id, JobType.OrdersImport, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncCheckpoint?)null);

        _ordersSource
            .Setup(s => s.GetOrdersAsync(connection.Id, It.IsAny<OrdersRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrdersPage
            {
                Orders = new List<ChannelOrderSnapshot> { BuildCart("ORDER-1", "SRID-1", "SRID-2") },
                HasMore = false,
            });

        await CreateConsumer().ProcessAsync(
            new OrdersImportRequestedEvent { JobRunId = firstJobRun.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        List<string> firstRunLineKeys = PublishedOrders()
            .Single(o => o.ExternalOrderId == "ORDER-1")
            .Lines
            .Select(l => l.LineKey)
            .ToList();

        _eventPublisher.Invocations.Clear();

        // The same window again, which is what a delta run inside the safety overlap produces.
        await CreateConsumer().ProcessAsync(
            new OrdersImportRequestedEvent { JobRunId = secondJobRun.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        List<string> secondRunLineKeys = PublishedOrders()
            .Single(o => o.ExternalOrderId == "ORDER-1")
            .Lines
            .Select(l => l.LineKey)
            .ToList();

        // The merge is by line key, so re-reading the cart cannot grow the line set.
        secondRunLineKeys.Should().BeEquivalentTo(firstRunLineKeys);
        secondRunLineKeys.Should().HaveCount(2);
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

    [Fact]
    public async Task ProcessAsync_RowWithoutIdentity_FailsTheRunAndPublishesNothing()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        JobRun jobRun = BuildJobRun(connection, JobStatus.Pending);

        SetupDefaults(jobRun);

        _syncCheckpointRepo
            .Setup(r => r.GetAsync(connection.Id, JobType.OrdersImport, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncCheckpoint?)null);

        // The source dropped a row it could not identify and reports how many. The remaining rows
        // are well-formed, but importing them would advance past the window that held the loss.
        _ordersSource
            .Setup(s => s.GetOrdersAsync(connection.Id, It.IsAny<OrdersRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrdersPage
            {
                Orders = BuildOrders(2),
                HasMore = false,
                RowsWithoutIdentity = 1,
            });

        ProcessOrdersImportConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(
            new OrdersImportRequestedEvent { JobRunId = jobRun.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        jobRun.Status.Should().Be(JobStatus.Failed);
        jobRun.Error.Should().Contain("without a usable order identity");

        _eventPublisher.Verify(
            p => p.PublishAsync(It.IsAny<OrdersBatchImportRequestedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);

        _jobCheckpointRepo.Verify(
            r => r.UpsertAsync(It.Is<JobCheckpoint>(checkpoint =>
                checkpoint.Payload.Contains("\"IsFetchCompleted\":false")), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_CartReportedWithThenWithoutGNumber_FailsInsteadOfDuplicating()
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
                    return new OrdersPage { Orders = new List<ChannelOrderSnapshot> { BuildOrder("G-1") }, HasMore = true };
                }

                // The same physical cart, now reported without the only field that identifies it. The
                // source cannot key it and says so; importing the first page anyway would leave the
                // cart under G-1 and the second observation either dropped or filed under a second
                // key on a later run.
                return new OrdersPage
                {
                    Orders = new List<ChannelOrderSnapshot>(),
                    HasMore = false,
                    RowsWithoutIdentity = 1,
                };
            });

        ProcessOrdersImportConsumer consumer = CreateConsumer(maxOrdersPerResponse: 1);

        await consumer.ProcessAsync(
            new OrdersImportRequestedEvent { JobRunId = jobRun.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        jobRun.Status.Should().Be(JobStatus.Failed);
        jobRun.Error.Should().Contain("without a usable order identity");

        // Nothing is published: a partially imported window cannot be told apart from one that was
        // fully imported, and the watermark would move past the cart.
        _eventPublisher.Verify(
            p => p.PublishAsync(It.IsAny<OrdersBatchImportRequestedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_SameOrderReportedByBothHalves_KeepsTheLaterReport()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        JobRun jobRun = BuildJobRun(connection, JobStatus.Pending);

        SetupDefaults(jobRun);

        _syncCheckpointRepo
            .Setup(r => r.GetAsync(connection.Id, JobType.OrdersImport, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncCheckpoint?)null);

        DateTimeOffset earlier = DateTimeOffset.UtcNow.AddMinutes(-30);
        DateTimeOffset later = DateTimeOffset.UtcNow.AddMinutes(-5);

        int call = 0;

        _ordersSource
            .Setup(s => s.GetOrdersAsync(connection.Id, It.IsAny<OrdersRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                call++;

                // The order sits on the instant the two halves share, so both report it. The first
                // half carries the newer report and the second half the older one, which is the
                // order that distinguishes a merge from an overwrite: keeping whichever snapshot
                // arrived last would publish the stale status. The halves are fetched in a fixed
                // order, so this ordering is what the traversal actually produces.
                if (call == 1)
                {
                    return new OrdersPage
                    {
                        Orders = new List<ChannelOrderSnapshot>
                        {
                            BuildOrder("ORDER-1") with
                            {
                                StatusChangedAt = later,
                                Lines = new List<ChannelOrderLineSnapshot> { BuildLine("ORDER-1", statusChangedAt: later) },
                            },
                        },
                        HasMore = true,
                    };
                }

                if (call == 2)
                {
                    return new OrdersPage
                    {
                        Orders = new List<ChannelOrderSnapshot>
                        {
                            BuildOrder("ORDER-1") with
                            {
                                StatusChangedAt = earlier,
                                Lines = new List<ChannelOrderLineSnapshot> { BuildLine("ORDER-1", statusChangedAt: earlier) },
                            },
                        },
                        HasMore = false,
                    };
                }

                return new OrdersPage { Orders = new List<ChannelOrderSnapshot>(), HasMore = false };
            });

        ProcessOrdersImportConsumer consumer = CreateConsumer(maxOrdersPerResponse: 1);

        await consumer.ProcessAsync(
            new OrdersImportRequestedEvent { JobRunId = jobRun.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        List<ChannelOrderSnapshot> published = CapturedBatches()
            .SelectMany(b => b.Orders)
            .ToList();

        published.Should().ContainSingle();
        published.Single().ExternalOrderId.Should().Be("ORDER-1");
        published.Single().StatusChangedAt.Should().Be(later);
    }

    /// <summary>
    /// Captures the payload the orchestrator persists, so assertions can read back the exact
    /// watermark the aggregator would later apply to SyncCheckpoint.
    /// </summary>
    private void CapturePersistedPayload(Action<OrdersImportCheckpointPayload> onCaptured)
    {
        _jobCheckpointRepo
            .Setup(r => r.UpsertAsync(It.IsAny<JobCheckpoint>(), It.IsAny<CancellationToken>()))
            .Callback<JobCheckpoint, CancellationToken>((checkpoint, _) =>
            {
                OrdersImportCheckpointPayload? payload =
                    JsonSerializer.Deserialize<OrdersImportCheckpointPayload>(checkpoint.Payload);

                if (payload != null)
                {
                    onCaptured(payload);
                }
            })
            .Returns(Task.CompletedTask);
    }

    /// <summary>
    /// Renders the Warning entries Moq recorded. The formatter stored in the log-state argument is
    /// the one Microsoft.Extensions.Logging would apply, so the message carries substituted
    /// placeholders rather than raw argument values.
    /// </summary>
    private List<string> WarningMessages()
    {
        List<string> messages = new List<string>();

        foreach (IInvocation invocation in _logger.Invocations)
        {
            if (invocation.Method.Name != nameof(ILogger.Log))
            {
                continue;
            }

            if (invocation.Arguments.Count > 2
                && invocation.Arguments[0] is LogLevel level
                && level == LogLevel.Warning
                && invocation.Arguments[2] is Func<string, Exception?, string> formatter)
            {
                messages.Add(formatter(invocation.Arguments[1].ToString() ?? string.Empty, null));
            }
        }

        return messages;
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

    private List<ChannelOrderSnapshot> PublishedOrders()
    {
        return CapturedBatches().SelectMany(b => b.Orders).ToList();
    }

    /// <summary>
    /// Drives one run against a source that answers from a script and records the requests it was
    /// asked. The regression this guards is a bisection that restarts the left half from the
    /// parent's own dateFrom, which the endpoint cannot distinguish from the parent request.
    /// </summary>
    private async Task<(JobRun JobRun, List<OrdersRequest> IssuedRequests)> RunWithScriptedSourceAsync(
        Func<int, OrdersRequest, OrdersPage> respond,
        int maxOrdersPerResponse,
        TimeSpan? minOrdersWindow = null,
        DateTimeOffset? lastChangedAt = null)
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        JobRun jobRun = BuildJobRun(connection, JobStatus.Pending);

        SetupDefaults(jobRun);

        _syncCheckpointRepo
            .Setup(r => r.GetAsync(connection.Id, JobType.OrdersImport, It.IsAny<CancellationToken>()))
            .ReturnsAsync(lastChangedAt == null
                ? (SyncCheckpoint?)null
                : new SyncCheckpoint
                {
                    Id = Guid.NewGuid(),
                    ConnectionId = connection.Id,
                    JobType = JobType.OrdersImport,
                    LastChangedAt = lastChangedAt,
                });

        List<OrdersRequest> issuedRequests = new List<OrdersRequest>();
        int call = 0;

        _ordersSource
            .Setup(s => s.GetOrdersAsync(connection.Id, It.IsAny<OrdersRequest>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, OrdersRequest, CancellationToken>((_, request, _) => issuedRequests.Add(request))
            .ReturnsAsync((Guid _, OrdersRequest request, CancellationToken _) =>
            {
                call++;

                return respond(call, request);
            });

        ProcessOrdersImportConsumer consumer = CreateConsumer(maxOrdersPerResponse, minOrdersWindow);

        await consumer.ProcessAsync(
            new OrdersImportRequestedEvent { JobRunId = jobRun.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        return (jobRun, issuedRequests);
    }

    [Fact]
    public async Task ProcessAsync_CappedResponse_ResumesFromCoveredBoundaryInsteadOfWindowStart()
    {
        DateTimeOffset windowFrom = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset coveredUntil = windowFrom.AddDays(10);

        (JobRun jobRun, List<OrdersRequest> issuedRequests) = await RunWithScriptedSourceAsync(
            (call, request) => call switch
            {
                // The first response hit the cap having served the first ten days only.
                1 => new OrdersPage
                {
                    Orders = BuildOrders(2, 1),
                    HasMore = true,
                    CoveredUntil = coveredUntil,
                },
                // Whatever is asked next returns cleanly; its own coverage decision is not the point.
                _ => new OrdersPage
                {
                    Orders = BuildOrders(1, 20 + call),
                    HasMore = false,
                    CoveredUntil = request.DateTo,
                },
            },
            maxOrdersPerResponse: 2,
            lastChangedAt: windowFrom);

        // The run completed its traversal: no failure, and exactly one fetch-completed event published.
        jobRun.Status.Should().NotBe(JobStatus.Failed);
        _eventPublisher.Verify(
            p => p.PublishAsync(
                It.IsAny<OrdersImportFetchCompletedEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Each followed window must start where the unknown region starts. A split at the window
        // midpoint reuses the parent's dateFrom and the endpoint replays the identical block.
        issuedRequests[1].DateFrom.Should().Be(coveredUntil);
        issuedRequests[1].DateFrom.Should().BeAfter(issuedRequests[0].DateFrom);

        // Left half, then right half: the recursion still covers the parent's whole window rather
        // than stopping at the coverage point.
        issuedRequests.Should().HaveCount(3);
        issuedRequests[issuedRequests.Count - 1].DateTo.Should().Be(issuedRequests[0].DateTo);
    }

    [Fact]
    public async Task ProcessAsync_CappedResponseCoveringWholeWindow_StopsWithoutNarrowing()
    {
        (JobRun jobRun, List<OrdersRequest> issuedRequests) = await RunWithScriptedSourceAsync(
            // The cap was hit, but every row it kept is past the window end, so the window itself is
            // fully served and there is nothing left to read above it.
            (call, request) => new OrdersPage
            {
                Orders = BuildOrders(2, 1),
                HasMore = true,
                CoveredUntil = request.DateTo,
            },
            maxOrdersPerResponse: 2,
            lastChangedAt: new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));

        issuedRequests.Should().ContainSingle();
        jobRun.Status.Should().NotBe(JobStatus.Failed);

        PublishedOrders().Should().HaveCount(2);
    }

    [Fact]
    public async Task ProcessAsync_CappedResponseWithoutCoverage_StillNarrowsAndRetries()
    {
        (JobRun jobRun, List<OrdersRequest> issuedRequests) = await RunWithScriptedSourceAsync(
            (call, request) => call == 1
                // The source could not prove how far it reached. Falling back to the requested
                // window start narrows and retries rather than assuming the window was served.
                ? new OrdersPage { Orders = BuildOrders(2, 1), HasMore = true, CoveredUntil = null }
                : new OrdersPage { Orders = BuildOrders(1, 10), HasMore = false, CoveredUntil = request.DateTo },
            maxOrdersPerResponse: 2,
            minOrdersWindow: TimeSpan.FromDays(20),
            lastChangedAt: new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));

        issuedRequests.Should().HaveCount(3);

        // Without a proven boundary the split is the plain one, which is the pre-fix behaviour.
        issuedRequests[1].DateFrom.Should().Be(issuedRequests[0].DateFrom);
    }

    [Fact]
    public async Task ProcessAsync_RemainingRegionAtMinimumWindow_FailsTheRun()
    {
        (JobRun jobRun, List<OrdersRequest> issuedRequests) = await RunWithScriptedSourceAsync(
            // Coverage stops ten minutes short of the window end, which is below the narrowest
            // window the endpoint can be asked for, so the remainder cannot be bisected at all.
            (call, request) => new OrdersPage
            {
                Orders = BuildOrders(2, 1),
                HasMore = true,
                CoveredUntil = request.DateTo.AddMinutes(-10),
            },
            maxOrdersPerResponse: 2,
            minOrdersWindow: TimeSpan.FromMinutes(30),
            lastChangedAt: DateTimeOffset.UtcNow.AddMinutes(-30));

        issuedRequests.Should().ContainSingle();

        DateTimeOffset unreachableFrom = issuedRequests[0].DateTo.AddMinutes(-10);

        jobRun.Status.Should().Be(JobStatus.Failed);
        jobRun.Error.Should().Contain("cannot narrow window");
        jobRun.Error.Should().Contain(unreachableFrom.ToString("O", CultureInfo.InvariantCulture));

        _eventPublisher.Verify(
            p => p.PublishAsync(It.IsAny<OrdersBatchImportRequestedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_CartSplitByResumeBoundary_PublishesOneOrderWithEveryLine()
    {
        DateTimeOffset coveredUntil = new DateTimeOffset(2024, 1, 2, 0, 0, 0, TimeSpan.Zero);

        (JobRun jobRun, List<OrdersRequest> issuedRequests) = await RunWithScriptedSourceAsync(
            (call, request) => call switch
            {
                // One cart, its units served either side of the boundary the resume starts from.
                1 => new OrdersPage
                {
                    Orders = new List<ChannelOrderSnapshot> { BuildCart("ORDER-1", "SRID-1") },
                    HasMore = true,
                    CoveredUntil = coveredUntil,
                },
                _ => new OrdersPage
                {
                    Orders = new List<ChannelOrderSnapshot> { BuildCart("ORDER-1", "SRID-2") },
                    HasMore = false,
                    CoveredUntil = request.DateTo,
                },
            },
            maxOrdersPerResponse: 1,
            lastChangedAt: new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));

        jobRun.Status.Should().NotBe(JobStatus.Failed);

        List<ChannelOrderSnapshot> cart = PublishedOrders().Where(o => o.ExternalOrderId == "ORDER-1").ToList();

        // Resuming from the boundary re-reads the cart once; it must stay one order with both units.
        cart.Should().ContainSingle();
        cart.Single().Lines.Select(l => l.LineKey).Should().BeEquivalentTo(new[] { "SRID-1", "SRID-2" });
    }
}
