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

/// <summary>
/// End-to-end tests for the Ozon FBO branch of the orders orchestrator, mirroring
/// <see cref="ProcessOrdersImportConsumerBackfillTests"/> and
/// <see cref="ProcessOrdersImportConsumerBackfillStopTests"/> but exercised through
/// <see cref="OzonOrdersFetchers"/>/<see cref="OzonBackfillOrdersFetcher"/>/<see cref="OzonDeltaOrdersFetcher"/>
/// resolved by (channel, job type) instead of the Wildberries pair.
/// </summary>
public class OzonOrdersImportEndToEndTests
{
    private static readonly TimeSpan ObservedChangeTimeOfDay = TimeSpan.FromHours(12);

    private readonly FakeTimeProvider _timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 10, 21, 9, 0, 0, TimeSpan.Zero));

    private readonly Mock<IJobRunRepository> _jobRunRepo = new();
    private readonly Mock<IJobCheckpointRepository> _jobCheckpointRepo = new();
    private readonly Mock<ISyncCheckpointRepository> _syncCheckpointRepo = new();
    private readonly Mock<IOrdersBackfillStateRepository> _backfillStateRepo = new();
    private readonly Mock<IChannelApiClientFactory> _clientFactory = new();
    private readonly Mock<IEventPublisher> _eventPublisher = new();
    private readonly Mock<ILogger<ProcessOrdersImportConsumer>> _logger = new();
    private readonly Mock<IOrdersChannelClient> _ordersSource = new();

    public interface IOrdersChannelClient : IChannelApiClient, IOrdersSource { }

    private readonly JobRunExecutionContext _jobRunExecutionContext = new();

    private readonly List<OrdersRequest> _issuedRequests = new();

    private OrdersBackfillState? _persistedState;

    private ProcessOrdersImportConsumer CreateConsumer(int backfillRunBudgetSeconds = 1200)
    {
        OzonImportOptions options = new OzonImportOptions
        {
            BackfillRunBudgetSeconds = backfillRunBudgetSeconds,
            MaxInProcessWaitSeconds = 0,
        };

        OrdersImportCheckpointStore checkpointStore = new OrdersImportCheckpointStore(
            _jobCheckpointRepo.Object,
            _timeProvider);

        OrdersBatchPublisher batchPublisher = new OrdersBatchPublisher(
            _eventPublisher.Object,
            checkpointStore);

        PacedOrdersSource pacedSource = new PacedOrdersSource(
            new AlwaysAllowOrdersRateLimiter(),
            _timeProvider);

        CursorPagedOrdersReader cursorReader = new CursorPagedOrdersReader(
            pacedSource,
            new Mock<ILogger<CursorPagedOrdersReader>>().Object,
            TimeSpan.FromSeconds(options.MaxInProcessWaitSeconds),
            OrdersRateLimiterMethods.OzonPostingFboList);

        OrdersBackfillWalker backfillWalker = new OrdersBackfillWalker(
            _backfillStateRepo.Object,
            batchPublisher,
            new Mock<ILogger<OrdersBackfillWalker>>().Object,
            _timeProvider);

        return new ProcessOrdersImportConsumer(
            _jobRunRepo.Object,
            checkpointStore,
            backfillWalker,
            StubOrdersFetcherResolver.ForOzon(
                new OzonBackfillOrdersFetcher(backfillWalker, cursorReader),
                new OzonDeltaOrdersFetcher(
                    cursorReader,
                    _syncCheckpointRepo.Object,
                    batchPublisher,
                    new Mock<ILogger<OzonDeltaOrdersFetcher>>().Object,
                    Options.Create(options)),
                cursorReader,
                options),
            _clientFactory.Object,
            _eventPublisher.Object,
            _jobRunExecutionContext,
            _logger.Object,
            _timeProvider,
            new JobRunLifecycle(_jobRunRepo.Object, _timeProvider),
            new OrdersContinuationScheduler(_jobRunRepo.Object, _timeProvider));
    }

    private static SystemChannelTemplate BuildOzonTemplate()
    {
        return new SystemChannelTemplate
        {
            Id = Guid.NewGuid(),
            Code = "ozon",
            DisplayName = "Ozon",
            ApiBaseUrl = "https://api-seller.ozon.ru",
            ApiVersion = "v3",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    private Connection BuildConnection(SystemChannelTemplate template)
    {
        return new Connection
        {
            Id = Guid.NewGuid(),
            ChannelId = Guid.NewGuid(),
            SystemChannelTemplateId = template.Id,
            Name = "Test Ozon",
            ApiKey = "test-ozon-key",
            IsActive = true,
            CreatedAt = _timeProvider.GetUtcNow(),
            SystemChannelTemplate = template,
        };
    }

    private JobRun BuildJobRun(Connection connection, JobType jobType = JobType.OrdersImportFbo)
    {
        return new JobRun
        {
            Id = Guid.NewGuid(),
            ConnectionId = connection.Id,
            JobType = jobType,
            Status = JobStatus.Pending,
            CreatedAt = _timeProvider.GetUtcNow(),
            Connection = connection,
        };
    }

    private static List<ChannelOrderSnapshot> BuildOrdersForDay(DateTimeOffset dayFrom, int count = 2)
    {
        string dayKey = dayFrom.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        List<ChannelOrderSnapshot> orders = new List<ChannelOrderSnapshot>();

        for (int i = 0; i < count; i++)
        {
            string externalOrderId = $"OZ-{dayKey}-{i}";

            orders.Add(new ChannelOrderSnapshot
            {
                ExternalOrderId = externalOrderId,
                ExternalOrderNumber = externalOrderId,
                OrderDate = dayFrom.AddHours(10),
                StatusChangedAt = dayFrom + ObservedChangeTimeOfDay,
                ExternalStatus = "awaiting_packaging",
                Currency = "RUB",
                Lines = new List<ChannelOrderLineSnapshot>
                {
                    new ChannelOrderLineSnapshot
                    {
                        LineKey = externalOrderId,
                        ExternalLineId = externalOrderId,
                        ExternalProductId = $"sku-{externalOrderId}",
                        OfferId = $"OFFER-{externalOrderId}",
                        ProductName = "Test Ozon Product",
                        Quantity = 1,
                        UnitPrice = 1000m,
                        BuyerUnitPrice = 950m,
                        UnitPriceBeforeDiscount = 1200m,
                        Currency = "RUB",
                        IsCancelled = false,
                        StatusChangedAt = dayFrom + ObservedChangeTimeOfDay,
                        ChannelReportedTotal = 1200m,
                    },
                },
            });
        }

        return orders;
    }

    private static OrdersBackfillState Clone(OrdersBackfillState state)
    {
        return new OrdersBackfillState
        {
            Id = state.Id,
            TenantId = state.TenantId,
            ConnectionId = state.ConnectionId,
            JobType = state.JobType,
            NextDayToFetch = state.NextDayToFetch,
            OldestDayInScope = state.OldestDayInScope,
            IsCompleted = state.IsCompleted,
            CompletedAt = state.CompletedAt,
            CatchUpNextDay = state.CatchUpNextDay,
            HandoverWatermark = state.HandoverWatermark,
        };
    }

    private void UseStateStore()
    {
        _backfillStateRepo
            .Setup(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<JobType>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _persistedState == null ? null : Clone(_persistedState));

        _backfillStateRepo
            .Setup(r => r.UpsertAsync(It.IsAny<OrdersBackfillState>(), It.IsAny<CancellationToken>()))
            .Callback<OrdersBackfillState, CancellationToken>((state, _) => _persistedState = Clone(state))
            .Returns(Task.CompletedTask);
    }

    private void UseSource(Func<OrdersRequest, int, Task<OrdersPage>> respond)
    {
        int call = 0;

        _ordersSource
            .Setup(s => s.GetOrdersAsync(It.IsAny<Guid>(), It.IsAny<OrdersRequest>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, OrdersRequest, CancellationToken>((_, request, _) => _issuedRequests.Add(request))
            .Returns((Guid _, OrdersRequest request, CancellationToken _) =>
            {
                call++;

                return respond(request, call);
            });
    }

    private void UseInstantCompleteSource()
    {
        UseSource((request, _) => Task.FromResult(new OrdersPage
        {
            Orders = BuildOrdersForDay(request.DateFrom),
            HasMore = false,
        }));
    }

    private void SetupRun(JobRun jobRun)
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

        _syncCheckpointRepo
            .Setup(r => r.GetAsync(It.IsAny<Guid>(), JobType.OrdersImportFbo, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncCheckpoint?)null);

        _clientFactory
            .Setup(f => f.Create(It.IsAny<SystemChannelTemplate>()))
            .Returns(_ordersSource.Object);
    }

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

    private async Task RunAsync(JobRun jobRun, Connection connection, ProcessOrdersImportConsumer consumer)
    {
        await consumer.ProcessAsync(
            new OrdersImportRequestedEvent { JobRunId = jobRun.Id, ConnectionId = connection.Id },
            CancellationToken.None);
    }

    [Fact]
    public async Task Backfill_WalksDayByDay_PublishesOneUnitPerDayAndStampsHandoverWatermark()
    {
        Connection connection = BuildConnection(BuildOzonTemplate());
        JobRun jobRun = BuildJobRun(connection);

        SetupRun(jobRun);
        UseStateStore();
        UseInstantCompleteSource();

        await RunAsync(jobRun, connection, CreateConsumer());

        _issuedRequests.Should().NotBeEmpty();
        _issuedRequests.Should().OnlyContain(request => request.Selection == OrdersSelectionMode.Placed);

        // One OrdersBatchImportRequestedEvent per day: each fixture day carries two orders, well
        // under the batch size, so the walk never splits a day across batches.
        _eventPublisher.Verify(
            p => p.PublishAsync(It.IsAny<OrdersBatchImportRequestedEvent>(), It.IsAny<CancellationToken>()),
            Times.Exactly(_issuedRequests.Count));

        _eventPublisher.Verify(
            p => p.PublishAsync(It.IsAny<OrdersImportFetchCompletedEvent>(), It.IsAny<CancellationToken>()),
            Times.Once);

        _persistedState.Should().NotBeNull();
        _persistedState!.IsCompleted.Should().BeTrue();
        _persistedState.HandoverWatermark.Should().NotBeNull();
        jobRun.Status.Should().NotBe(JobStatus.Failed);
    }

    [Fact]
    public async Task Backfill_ThrottledMidRun_PersistsCheckpointAndSchedulesContinuationWithSameJobType()
    {
        Connection connection = BuildConnection(BuildOzonTemplate());
        JobRun jobRun = BuildJobRun(connection);
        OrdersImportCheckpointPayload? persistedPayload = null;
        DateTimeOffset retryAt = _timeProvider.GetUtcNow().AddHours(3);

        SetupRun(jobRun);
        CapturePersistedPayload(payload => persistedPayload = payload);
        UseStateStore();

        UseSource((request, call) => Task.FromResult(call == 1
            ? new OrdersPage { Orders = BuildOrdersForDay(request.DateFrom), HasMore = false }
            : new OrdersPage { Orders = new List<ChannelOrderSnapshot>(), Throttled = true, RetryAt = retryAt }));

        await RunAsync(jobRun, connection, CreateConsumer());

        _eventPublisher.Verify(
            p => p.PublishAsync(It.IsAny<OrdersImportFetchCompletedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);

        persistedPayload.Should().NotBeNull();
        persistedPayload!.IsFetchCompleted.Should().BeFalse();
        persistedPayload.IsBackfillCompleted.Should().BeFalse();

        _jobRunRepo.Verify(
            r => r.CreateAsync(
                It.Is<JobRun>(next =>
                    next.Status == JobStatus.Pending
                    && next.JobType == JobType.OrdersImportFbo
                    && next.ConnectionId == connection.Id
                    && next.NotBefore == retryAt),
                It.IsAny<CancellationToken>()),
            Times.Once);

        jobRun.Status.Should().NotBe(JobStatus.Failed);
    }

    [Fact]
    public async Task Delta_AfterHandover_ReadsFromWatermarkAndAdvancesMaxChangedAt()
    {
        Connection connection = BuildConnection(BuildOzonTemplate());
        JobRun jobRun = BuildJobRun(connection);
        DateTimeOffset today = OrdersWindowPlanner.GetMoscowDayStart(_timeProvider.GetUtcNow());
        DateTimeOffset handoverWatermark = today.AddDays(-1);

        _persistedState = new OrdersBackfillState
        {
            Id = Guid.NewGuid(),
            ConnectionId = connection.Id,
            JobType = JobType.OrdersImportFbo,
            OldestDayInScope = today.AddDays(-180),
            NextDayToFetch = today.AddDays(-181),
            IsCompleted = true,
            CompletedAt = _timeProvider.GetUtcNow(),
            HandoverWatermark = handoverWatermark,
        };

        SetupRun(jobRun);
        UseStateStore();

        DateTimeOffset latestOrderChange = _timeProvider.GetUtcNow().AddHours(-1);
        DateTimeOffset latestOrderDate = latestOrderChange.AddHours(-2);

        UseSource((request, call) => Task.FromResult(new OrdersPage
        {
            Orders = new List<ChannelOrderSnapshot>
            {
                new ChannelOrderSnapshot
                {
                    ExternalOrderId = "OZ-DELTA-1",
                    ExternalOrderNumber = "OZ-DELTA-1",
                    OrderDate = latestOrderChange.AddHours(-2),
                    StatusChangedAt = latestOrderChange,
                    ExternalStatus = "delivering",
                    Currency = "RUB",
                    Lines = new List<ChannelOrderLineSnapshot>
                    {
                        new ChannelOrderLineSnapshot
                        {
                            LineKey = "OZ-DELTA-1",
                            ExternalLineId = "OZ-DELTA-1",
                            ExternalProductId = "sku-OZ-DELTA-1",
                            OfferId = "OFFER-OZ-DELTA-1",
                            ProductName = "Test Ozon Product",
                            Quantity = 1,
                            UnitPrice = 1000m,
                            BuyerUnitPrice = 950m,
                            UnitPriceBeforeDiscount = 1200m,
                            Currency = "RUB",
                            IsCancelled = false,
                            StatusChangedAt = latestOrderChange,
                            ChannelReportedTotal = 1200m,
                        },
                    },
                },
            },
            HasMore = false,
        }));

        OrdersImportFetchCompletedEvent? completedEvent = null;
        _eventPublisher
            .Setup(p => p.PublishAsync(It.IsAny<OrdersImportFetchCompletedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<OrdersImportFetchCompletedEvent, CancellationToken>((evt, _) => completedEvent = evt)
            .Returns(Task.CompletedTask);

        await RunAsync(jobRun, connection, CreateConsumer());

        _issuedRequests.Should().ContainSingle();
        _issuedRequests[0].DateFrom.Should().Be(handoverWatermark);
        _issuedRequests[0].Selection.Should().Be(OrdersSelectionMode.LastChanged);
        _issuedRequests[0].Schema.Should().Be(OrdersSchema.Fbo);
        _issuedRequests[0].Statuses.Should().NotBeNull();

        completedEvent.Should().NotBeNull();
        completedEvent!.IsBackfill.Should().BeFalse();

        // The watermark is created_at, the only instant posting/fbo/list filters on: the change
        // instant of the same posting is deliberately later and must not move the window.
        completedEvent.MaxChangedAt.Should().Be(latestOrderDate);

        jobRun.Status.Should().NotBe(JobStatus.Failed);
    }

    [Fact]
    public async Task Delta_Stopped_NeverAdvancesTheWatermark()
    {
        Connection connection = BuildConnection(BuildOzonTemplate());
        JobRun jobRun = BuildJobRun(connection);
        DateTimeOffset today = OrdersWindowPlanner.GetMoscowDayStart(_timeProvider.GetUtcNow());
        DateTimeOffset handoverWatermark = today.AddDays(-1);
        DateTimeOffset retryAt = _timeProvider.GetUtcNow().AddHours(3);

        _persistedState = new OrdersBackfillState
        {
            Id = Guid.NewGuid(),
            ConnectionId = connection.Id,
            JobType = JobType.OrdersImportFbo,
            OldestDayInScope = today.AddDays(-180),
            NextDayToFetch = today.AddDays(-181),
            IsCompleted = true,
            CompletedAt = _timeProvider.GetUtcNow(),
            HandoverWatermark = handoverWatermark,
        };

        SetupRun(jobRun);
        UseStateStore();

        OrdersImportCheckpointPayload? persistedPayload = null;
        CapturePersistedPayload(payload => persistedPayload = payload);

        UseSource((request, call) => Task.FromResult(
            new OrdersPage { Orders = new List<ChannelOrderSnapshot>(), Throttled = true, RetryAt = retryAt }));

        await RunAsync(jobRun, connection, CreateConsumer());

        _eventPublisher.Verify(
            p => p.PublishAsync(It.IsAny<OrdersImportFetchCompletedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);

        _eventPublisher.Verify(
            p => p.PublishAsync(It.IsAny<OrdersBatchImportRequestedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);

        persistedPayload.Should().NotBeNull();
        persistedPayload!.MaxChangedAt.Should().BeNull();
        persistedPayload.IsFetchCompleted.Should().BeFalse();

        // Backfill state is unaffected by a delta stop: the handover watermark set at backfill
        // completion is the only watermark there is until a delta run completes.
        _persistedState!.HandoverWatermark.Should().Be(handoverWatermark);

        jobRun.Status.Should().NotBe(JobStatus.Failed);
    }

    [Fact]
    public async Task UnsupportedChannelJobTypeCombination_FailsTheRunWithTheNewMessage()
    {
        Connection connection = BuildConnection(BuildOzonTemplate());
        JobRun jobRun = BuildJobRun(connection, JobType.OrdersImportFbs);

        SetupRun(jobRun);

        OzonImportOptions options = new OzonImportOptions();

        OrdersImportCheckpointStore checkpointStore = new OrdersImportCheckpointStore(
            _jobCheckpointRepo.Object,
            _timeProvider);

        OrdersBatchPublisher batchPublisher = new OrdersBatchPublisher(_eventPublisher.Object, checkpointStore);

        OrdersBackfillWalker backfillWalker = new OrdersBackfillWalker(
            _backfillStateRepo.Object,
            batchPublisher,
            new Mock<ILogger<OrdersBackfillWalker>>().Object,
            _timeProvider);

        ProcessOrdersImportConsumer consumer = new ProcessOrdersImportConsumer(
            _jobRunRepo.Object,
            checkpointStore,
            backfillWalker,
            StubOrdersFetcherResolver.Unsupported(),
            _clientFactory.Object,
            _eventPublisher.Object,
            _jobRunExecutionContext,
            _logger.Object,
            _timeProvider,
            new JobRunLifecycle(_jobRunRepo.Object, _timeProvider),
            new OrdersContinuationScheduler(_jobRunRepo.Object, _timeProvider));

        await RunAsync(jobRun, connection, consumer);

        jobRun.Status.Should().Be(JobStatus.Failed);
        jobRun.Error.Should().Contain("does not support orders import for job type");
        jobRun.Error.Should().Contain("ozon");
        jobRun.Error.Should().Contain(nameof(JobType.OrdersImportFbs));

        _jobRunRepo.Verify(r => r.CreateAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
