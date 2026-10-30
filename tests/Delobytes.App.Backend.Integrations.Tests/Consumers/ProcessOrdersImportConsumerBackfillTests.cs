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
/// Tests for the time-budgeted day-by-day backfill branch of the orders orchestrator: the cursor a
/// fresh connection starts from, the one-request-per-calendar-day shape of the walk, where the run
/// stops and why, and what the cursor and the checkpoint payload look like afterwards.
/// </summary>
public partial class ProcessOrdersImportConsumerBackfillTests
{
    private static readonly TimeSpan MoscowOffset = TimeSpan.FromHours(3);

    /// <summary>
    /// Fixed instant the fake source reports as every order's own change time: the run window is
    /// deliberately not fixed, so the only stable watermark expectation is one derived from a day.
    /// </summary>
    private static readonly TimeSpan ObservedChangeTimeOfDay = TimeSpan.FromHours(12);

    private readonly FakeTimeProvider _timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 10, 21, 9, 0, 0, TimeSpan.Zero));

    private readonly Mock<IJobRunRepository> _jobRunRepo = new();
    private readonly Mock<IJobCheckpointRepository> _jobCheckpointRepo = new();
    private readonly Mock<ISyncCheckpointRepository> _syncCheckpointRepo = new();
    private readonly Mock<IOrdersBackfillStateRepository> _backfillStateRepo = new();
    private readonly Mock<IChannelApiClientFactory> _clientFactory = new();
    private readonly Mock<IEventPublisher> _eventPublisher = new();
    private readonly Mock<ILogger<ProcessOrdersImportConsumer>> _logger = new();

    private readonly Mock<ILogger<BisectingOrdersReader>> _bisectingReaderLogger = new();
    private readonly Mock<ILogger<OrdersBackfillWalker>> _backfillWalkerLogger = new();
    private readonly Mock<IOrdersChannelClient> _ordersSource = new();

    public interface IOrdersChannelClient : IChannelApiClient, IOrdersSource { }

    // Real instance: JobRunExecutionContext has no virtual members for Moq to override and is
    // AsyncLocal-backed, so exercising it for real costs nothing.
    private readonly JobRunExecutionContext _jobRunExecutionContext = new();

    private readonly List<OrdersRequest> _issuedRequests = new();

    /// <summary>
    /// The persisted cursor. Held separately from the mock so a run that advances the cursor in
    /// memory without persisting it is observably different from one that persisted it: GetAsync
    /// hands out a copy, exactly as EF would hand out a fresh tracked instance.
    /// </summary>
    private OrdersBackfillState? _persistedState;

    private ProcessOrdersImportConsumer CreateConsumer(
        int backfillRunBudgetSeconds = 1200,
        int maxOrdersPerResponse = 80000)
    {
        WildberriesImportOptions options = new WildberriesImportOptions
        {
            MaxOrdersPerResponse = maxOrdersPerResponse,
            MinOrdersWindow = TimeSpan.FromHours(1),
            BackfillRunBudgetSeconds = backfillRunBudgetSeconds,
        };

        OrdersImportCheckpointStore checkpointStore = new OrdersImportCheckpointStore(
            _jobCheckpointRepo.Object,
            _timeProvider);

        OrdersBatchPublisher batchPublisher = new OrdersBatchPublisher(
            _eventPublisher.Object,
            checkpointStore);

        AlwaysAllowWildberriesRateLimiter rateLimiter = new AlwaysAllowWildberriesRateLimiter();
        IOptions<WildberriesImportOptions> wrappedOptions = Options.Create(options);

        PacedOrdersSource pacedSource = new PacedOrdersSource(
            rateLimiter,
            _timeProvider,
            wrappedOptions);

        BisectingOrdersReader bisectingReader = new BisectingOrdersReader(
            pacedSource,
            _bisectingReaderLogger.Object,
            wrappedOptions);

        OrdersBackfillWalker backfillWalker = new OrdersBackfillWalker(
            pacedSource,
            _backfillStateRepo.Object,
            batchPublisher,
            _backfillWalkerLogger.Object,
            _timeProvider);

        return new ProcessOrdersImportConsumer(
            _jobRunRepo.Object,
            checkpointStore,
            batchPublisher,
            bisectingReader,
            backfillWalker,
            _syncCheckpointRepo.Object,
            _clientFactory.Object,
            _eventPublisher.Object,
            _jobRunExecutionContext,
            wrappedOptions,
            _logger.Object,
            _timeProvider,
            new JobRunLifecycle(_jobRunRepo.Object, _timeProvider),
            new OrdersContinuationScheduler(_jobRunRepo.Object, _timeProvider));
    }

    private SystemChannelTemplate BuildWildberriesTemplate()
    {
        return new SystemChannelTemplate
        {
            Id = Guid.NewGuid(),
            Code = "wildberries",
            DisplayName = "Wildberries",
            ApiBaseUrl = "https://suppliers-api.wildberries.ru",
            ApiVersion = "v3",
            IsActive = true,
            CreatedAt = _timeProvider.GetUtcNow(),
        };
    }

    private Connection BuildConnection(SystemChannelTemplate template)
    {
        return new Connection
        {
            Id = Guid.NewGuid(),
            ChannelId = Guid.NewGuid(),
            SystemChannelTemplateId = template.Id,
            Name = "Test WB",
            ApiKey = "test-key",
            IsActive = true,
            CreatedAt = _timeProvider.GetUtcNow(),
            SystemChannelTemplate = template,
        };
    }

    private JobRun BuildJobRun(Connection connection)
    {
        return new JobRun
        {
            Id = Guid.NewGuid(),
            ConnectionId = connection.Id,
            JobType = JobType.OrdersImport,
            Status = JobStatus.Pending,
            CreatedAt = _timeProvider.GetUtcNow(),
            Connection = connection,
        };
    }

    /// <summary>
    /// Builds the orders one calendar day holds. The identity is derived from the day itself rather
    /// than from a counter, so the same day yields the same orders when a later run re-fetches it:
    /// that is what makes the crash-safety and multi-run cases assertable at all.
    /// </summary>
    private static List<ChannelOrderSnapshot> BuildOrdersForDay(DateTimeOffset dayFrom, int count = 2)
    {
        string dayKey = dayFrom.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        List<ChannelOrderSnapshot> orders = new List<ChannelOrderSnapshot>();

        for (int i = 0; i < count; i++)
        {
            string externalOrderId = $"ORDER-{dayKey}-{i}";

            orders.Add(new ChannelOrderSnapshot
            {
                ExternalOrderId = externalOrderId,
                ExternalOrderNumber = externalOrderId,
                OrderDate = dayFrom.AddHours(10),
                StatusChangedAt = dayFrom + ObservedChangeTimeOfDay,
                ExternalStatus = "new",
                Currency = "RUB",
                Lines = new List<ChannelOrderLineSnapshot>
                {
                    new ChannelOrderLineSnapshot
                    {
                        LineKey = externalOrderId,
                        ExternalLineId = externalOrderId,
                        ExternalProductId = $"nm-{externalOrderId}",
                        OfferId = $"SKU-{externalOrderId}",
                        ProductName = "Test Product",
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

    private static DateTimeOffset GetMoscowDayStart(DateTimeOffset instant)
    {
        DateTimeOffset moscow = instant.ToOffset(MoscowOffset);

        return new DateTimeOffset(moscow.Year, moscow.Month, moscow.Day, 0, 0, 0, MoscowOffset);
    }

    private static int GetDayCountInScope()
    {
        return (int)ProcessOrdersImportConsumer.BackfillWindow.TotalDays + 1;
    }

    private static OrdersBackfillState Clone(OrdersBackfillState state)
    {
        return new OrdersBackfillState
        {
            Id = state.Id,
            TenantId = state.TenantId,
            ConnectionId = state.ConnectionId,
            NextDayToFetch = state.NextDayToFetch,
            OldestDayInScope = state.OldestDayInScope,
            IsCompleted = state.IsCompleted,
            CompletedAt = state.CompletedAt,
            CatchUpNextDay = state.CatchUpNextDay,
            HandoverWatermark = state.HandoverWatermark,
        };
    }

    /// <summary>
    /// Backs the cursor repository with <see cref="_persistedState"/>. A null store models a
    /// connection that has never been backfilled, which is what makes the orchestrator create the
    /// cursor itself.
    /// </summary>
    private void UseStateStore()
    {
        _backfillStateRepo
            .Setup(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _persistedState == null ? null : Clone(_persistedState));

        _backfillStateRepo
            .Setup(r => r.UpsertAsync(It.IsAny<OrdersBackfillState>(), It.IsAny<CancellationToken>()))
            .Callback<OrdersBackfillState, CancellationToken>((state, _) => _persistedState = Clone(state))
            .Returns(Task.CompletedTask);
    }

    /// <summary>
    /// Answers each request from a script keyed by the 1-based call number, recording every request
    /// the orchestrator issued.
    /// </summary>
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

    /// <summary>
    /// A source that serves every day in full and immediately, which is the shape the budget and
    /// scope-boundary cases need.
    /// </summary>
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

    private List<string> PublishedOrderIds()
    {
        List<string> ids = new List<string>();

        foreach (IInvocation invocation in _eventPublisher.Invocations)
        {
            if (invocation.Arguments.Count > 0
                && invocation.Arguments[0] is OrdersBatchImportRequestedEvent published)
            {
                ids.AddRange(published.Orders.Select(order => order.ExternalOrderId));
            }
        }

        return ids;
    }

    [Fact]
    public async Task ProcessAsync_FreshConnection_FirstRequestTargetsTodayAndWalksBackwards()
    {
        DateTimeOffset expectedToday = GetMoscowDayStart(_timeProvider.GetUtcNow());

        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        JobRun jobRun = BuildJobRun(connection);

        SetupRun(jobRun);
        UseStateStore();
        UseInstantCompleteSource();

        ProcessOrdersImportConsumer consumer = CreateConsumer();

        await RunAsync(jobRun, connection, consumer);

        // The defect this work removes was a single flag=1 request for a 90-day window, which
        // selects one calendar day out of the whole span. The walk has to start at today, not at
        // now - 90 days, so recent orders land first.
        _issuedRequests.Should().NotBeEmpty();
        _issuedRequests[0].Flag.Should().Be(1);
        _issuedRequests[0].DateFrom.Should().Be(expectedToday);
    }

    [Fact]
    public async Task ProcessAsync_FreshConnection_IssuesOneSingleDayRequestPerCalendarDay()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        JobRun jobRun = BuildJobRun(connection);

        SetupRun(jobRun);
        UseStateStore();
        UseInstantCompleteSource();

        ProcessOrdersImportConsumer consumer = CreateConsumer();

        await RunAsync(jobRun, connection, consumer);

        int expectedDays = GetDayCountInScope();

        _issuedRequests.Should().HaveCount(expectedDays + 1);

        for (int i = 0; i < _issuedRequests.Count; i++)
        {
            OrdersRequest request = _issuedRequests[i];

            // flag=1 is a single-day selector, not a range filter: a window straddling midnight
            // would make the client throw, and a window that looked served while covering several
            // days would claim coverage it never fetched.
            request.Flag.Should().Be(1);
            request.DateTo.Should().Be(request.DateFrom.AddDays(1).AddSeconds(-1));
            request.DateFrom.Should().Be(i < expectedDays ? _issuedRequests[0].DateFrom.AddDays(-i) : _issuedRequests[0].DateFrom);
            request.Limit.Should().Be(0);
        }
    }

    [Fact]
    public async Task ProcessAsync_FreshConnection_CompletesWalkAndCatchUpWithHandoverWatermark()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        JobRun jobRun = BuildJobRun(connection);
        OrdersImportCheckpointPayload? persistedPayload = null;

        SetupRun(jobRun);
        CapturePersistedPayload(payload => persistedPayload = payload);
        UseStateStore();
        UseInstantCompleteSource();

        ProcessOrdersImportConsumer consumer = CreateConsumer();

        await RunAsync(jobRun, connection, consumer);

        int expectedDays = GetDayCountInScope();
        DateTimeOffset newestDay = _issuedRequests[0].DateFrom;
        DateTimeOffset oldestDay = newestDay.AddDays(-(expectedDays - 1));

        _issuedRequests[expectedDays - 1].DateFrom.Should().Be(oldestDay);
        _issuedRequests.Last().DateFrom.Should().Be(newestDay);

        _persistedState.Should().NotBeNull();
        _persistedState!.IsCompleted.Should().BeTrue();
        _persistedState.CompletedAt.Should().NotBeNull();
        _persistedState.NextDayToFetch.Should().Be(oldestDay.AddDays(-1));

        List<string> publishedIds = PublishedOrderIds();

        publishedIds.Should().HaveCount((expectedDays + 1) * 2);
        publishedIds.Distinct().Should().HaveCount(expectedDays * 2);

        persistedPayload.Should().NotBeNull();
        persistedPayload!.IsBackfill.Should().BeTrue();
        persistedPayload.IsBackfillCompleted.Should().BeTrue();
        persistedPayload.PagesFetched.Should().Be(expectedDays + 1);

        persistedPayload.HandoverWatermark.Should().Be(_timeProvider.GetUtcNow() - ProcessOrdersImportConsumer.SafetyOverlap);
        _persistedState.CatchUpNextDay.Should().Be(newestDay);
        persistedPayload.MaxChangedAt.Should().BeNull();
    }

    [Fact]
    public async Task ProcessAsync_ThrottledMidLoop_KeepsFetchedDaysAndLeavesCursorOnFirstUnfetchedDay()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        JobRun jobRun = BuildJobRun(connection);
        OrdersImportCheckpointPayload? persistedPayload = null;

        SetupRun(jobRun);
        CapturePersistedPayload(payload => persistedPayload = payload);
        UseStateStore();

        UseSource((request, call) => Task.FromResult(call == 1
            ? new OrdersPage { Orders = BuildOrdersForDay(request.DateFrom), HasMore = false }
            : new OrdersPage { Orders = new List<ChannelOrderSnapshot>(), Throttled = true, RetryAt = _timeProvider.GetUtcNow().AddHours(3) }));

        ProcessOrdersImportConsumer consumer = CreateConsumer();

        await RunAsync(jobRun, connection, consumer);

        // The throttled day was never fetched. A throttle is a scheduling fact, not a failure: the
        // run ends cleanly and the next scheduled run retries the same day.
        _issuedRequests.Should().HaveCount(2);

        DateTimeOffset fetchedDay = _issuedRequests[0].DateFrom;
        DateTimeOffset unfetchedDay = _issuedRequests[1].DateFrom;

        unfetchedDay.Should().Be(fetchedDay.AddDays(-1));

        string fetchedDayKey = fetchedDay.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

        PublishedOrderIds().Should().HaveCount(2).And.OnlyContain(id => id.Contains(
            fetchedDayKey,
            StringComparison.Ordinal));

        jobRun.Status.Should().NotBe(JobStatus.Failed);

        _persistedState.Should().NotBeNull();
        _persistedState!.IsCompleted.Should().BeFalse();
        _persistedState.NextDayToFetch.Should().Be(unfetchedDay);

        persistedPayload.Should().NotBeNull();
        persistedPayload!.IsBackfill.Should().BeTrue();
        persistedPayload.IsBackfillCompleted.Should().BeFalse();
        persistedPayload.MaxChangedAt.Should().BeNull();
        _jobRunRepo.Verify(r => r.CreateAsync(It.Is<JobRun>(next =>
            next.ConnectionId == connection.Id && next.JobType == JobType.OrdersImport
            && next.Status == JobStatus.Pending && next.NotBefore == _timeProvider.GetUtcNow().AddHours(3)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_ThrottledOnFirstDay_PersistsInitialCursorAndDefersContinuation()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        JobRun jobRun = BuildJobRun(connection);

        SetupRun(jobRun);
        UseStateStore();

        UseSource((_, _) => Task.FromResult(new OrdersPage
        {
            Orders = new List<ChannelOrderSnapshot>(),
            Throttled = true,
            RetryAt = _timeProvider.GetUtcNow().AddHours(3),
        }));

        ProcessOrdersImportConsumer consumer = CreateConsumer();

        await RunAsync(jobRun, connection, consumer);

        _issuedRequests.Should().ContainSingle();

        _backfillStateRepo.Verify(
            r => r.UpsertAsync(It.IsAny<OrdersBackfillState>(), It.IsAny<CancellationToken>()),
            Times.Once);

        _persistedState.Should().NotBeNull();
        _persistedState!.NextDayToFetch.Should().Be(GetMoscowDayStart(_timeProvider.GetUtcNow()));
    }

    [Fact]
    public async Task ProcessAsync_BudgetExpires_StopsBeforeTheNextDayAndKeepsConsistentState()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        JobRun jobRun = BuildJobRun(connection);
        OrdersImportCheckpointPayload? persistedPayload = null;

        SetupRun(jobRun);
        CapturePersistedPayload(payload => persistedPayload = payload);
        UseStateStore();

        // The second day is served slowly enough that the one-second budget is spent by the time
        // the loop turns to the third: the stop is caused by the budget alone, not by the throttle.
        UseSource((request, call) =>
        {
            if (call == 2)
            {
                _timeProvider.Advance(TimeSpan.FromSeconds(1));
            }

            return Task.FromResult(new OrdersPage { Orders = BuildOrdersForDay(request.DateFrom), HasMore = false });
        });

        ProcessOrdersImportConsumer consumer = CreateConsumer(backfillRunBudgetSeconds: 1);

        await RunAsync(jobRun, connection, consumer);

        _issuedRequests.Should().HaveCount(2);

        DateTimeOffset newestDay = _issuedRequests[0].DateFrom;
        DateTimeOffset firstUnfetchedDay = newestDay.AddDays(-2);

        _persistedState.Should().NotBeNull();
        _persistedState!.IsCompleted.Should().BeFalse();
        _persistedState.NextDayToFetch.Should().Be(firstUnfetchedDay);

        persistedPayload.Should().NotBeNull();
        persistedPayload!.IsBackfillCompleted.Should().BeFalse();
        persistedPayload.PagesFetched.Should().Be(2);
        _jobRunRepo.Verify(r => r.CreateAsync(It.Is<JobRun>(next =>
            next.Status == JobStatus.Pending && next.ConnectionId == connection.Id
            && next.NotBefore >= _timeProvider.GetUtcNow()), It.IsAny<CancellationToken>()), Times.Once);

        PublishedOrderIds().Should().HaveCount(4);
    }

    [Fact]
    public async Task ProcessAsync_DayTruncatedAtResponseCap_FailsTheRunWithoutPublishing()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        JobRun jobRun = BuildJobRun(connection);

        OrdersImportCheckpointPayload? persistedPayload = null;

        SetupRun(jobRun);
        CapturePersistedPayload(payload => persistedPayload = payload);
        UseStateStore();

        UseSource((request, call) => Task.FromResult(call == 1
            ? new OrdersPage { Orders = BuildOrdersForDay(request.DateFrom), HasMore = true }
            : new OrdersPage { Orders = BuildOrdersForDay(request.DateFrom), HasMore = false }));

        ProcessOrdersImportConsumer consumer = CreateConsumer(maxOrdersPerResponse: 2);

        await RunAsync(jobRun, connection, consumer);

        // A day is the floor this endpoint can be asked for, so a partial day cannot be narrowed
        // and re-requested. Importing what came back would silently drop orders, which is exactly
        // the defect this work removes.
        jobRun.Status.Should().Be(JobStatus.Failed);
        jobRun.Error.Should().Contain(_issuedRequests[0].DateFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        _issuedRequests.Should().ContainSingle();

        _eventPublisher.Verify(
            p => p.PublishAsync(It.IsAny<OrdersBatchImportRequestedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);

        _jobCheckpointRepo.Verify(
            r => r.UpsertAsync(It.IsAny<JobCheckpoint>(), It.IsAny<CancellationToken>()),
            Times.Once);

        persistedPayload.Should().NotBeNull();
        persistedPayload!.IsFetchCompleted.Should().BeFalse();
        persistedPayload.TotalBatches.Should().Be(0);
        persistedPayload.IsBackfill.Should().BeTrue();
        persistedPayload.IsBackfillCompleted.Should().BeFalse();
        persistedPayload.HandoverWatermark.Should().BeNull();

        _persistedState.Should().NotBeNull();
        _persistedState!.NextDayToFetch.Should().Be(GetMoscowDayStart(_timeProvider.GetUtcNow()));
    }

    [Fact]
    public async Task ProcessAsync_DayWithUnidentifiedRow_FailsTheRunAndPublishesNothing()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        JobRun jobRun = BuildJobRun(connection);

        SetupRun(jobRun);
        UseStateStore();

        UseSource((request, _) => Task.FromResult(new OrdersPage
        {
            Orders = BuildOrdersForDay(request.DateFrom),
            HasMore = false,
            RowsWithoutIdentity = 1,
        }));

        ProcessOrdersImportConsumer consumer = CreateConsumer();

        await RunAsync(jobRun, connection, consumer);

        jobRun.Status.Should().Be(JobStatus.Failed);
        jobRun.Error.Should().Contain("without a usable order identity");

        _eventPublisher.Verify(
            p => p.PublishAsync(It.IsAny<OrdersBatchImportRequestedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);

        _persistedState.Should().NotBeNull();
        _persistedState!.NextDayToFetch.Should().Be(GetMoscowDayStart(_timeProvider.GetUtcNow()));
    }

    [Fact]
    public async Task ProcessAsync_CursorIsPersistedOnlyAfterBatchesArePublished()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        JobRun jobRun = BuildJobRun(connection);
        List<string> trace = new List<string>();

        SetupRun(jobRun);
        UseStateStore();

        _eventPublisher
            .Setup(p => p.PublishAsync(It.IsAny<OrdersBatchImportRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<OrdersBatchImportRequestedEvent, CancellationToken>((_, _) => trace.Add("publish"))
            .Returns(Task.CompletedTask);

        _backfillStateRepo
            .Setup(r => r.UpsertAsync(It.IsAny<OrdersBackfillState>(), It.IsAny<CancellationToken>()))
            .Callback<OrdersBackfillState, CancellationToken>((state, _) =>
            {
                trace.Add("persist");
                _persistedState = Clone(state);
            })
            .Returns(Task.CompletedTask);

        UseSource((request, call) => Task.FromResult(call == 1
            ? new OrdersPage { Orders = BuildOrdersForDay(request.DateFrom), HasMore = false }
            : new OrdersPage { Orders = new List<ChannelOrderSnapshot>(), Throttled = true, RetryAt = _timeProvider.GetUtcNow().AddHours(3) }));

        ProcessOrdersImportConsumer consumer = CreateConsumer();

        await RunAsync(jobRun, connection, consumer);

        trace.Should().NotBeEmpty();

        // A cursor that ran ahead of the published batches would lose those days silently and
        // unrecoverably; persisting last costs a re-fetch on a crash, which the idempotent upsert
        // absorbs. The trace stops at the second persist because a run stopped by a throttle no
        // longer publishes a terminal batch (stage 10c).
        trace.Should().Equal("persist", "publish", "persist");
    }

    [Fact]
    public async Task ProcessAsync_CrashBeforePersisting_NextRunRefetchesSameDaysAndYieldsSameOrders()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);

        JobRun firstRun = BuildJobRun(connection);
        SetupRun(firstRun);
        UseStateStore();

        _backfillStateRepo
            .Setup(r => r.UpsertAsync(It.IsAny<OrdersBackfillState>(), It.IsAny<CancellationToken>()))
            .Callback<OrdersBackfillState, CancellationToken>((state, _) =>
            {
                if (_persistedState != null)
                {
                    throw new InvalidOperationException("connection lost while persisting the cursor");
                }

                _persistedState = Clone(state);
            })
            .Returns(Task.CompletedTask);

        UseSource((request, call) => Task.FromResult(call <= 3
            ? new OrdersPage { Orders = BuildOrdersForDay(request.DateFrom), HasMore = false }
            : new OrdersPage { Orders = new List<ChannelOrderSnapshot>(), Throttled = true, RetryAt = _timeProvider.GetUtcNow().AddHours(3) }));

        ProcessOrdersImportConsumer consumer = CreateConsumer();

        await RunAsync(firstRun, connection, consumer);

        firstRun.Status.Should().Be(JobStatus.Failed);
        _persistedState.Should().NotBeNull();
        _persistedState!.NextDayToFetch.Should().Be(_issuedRequests[0].DateFrom);

        List<string> firstRunOrderIds = PublishedOrderIds();
        firstRunOrderIds.Should().HaveCount(2);

        // Second run against a store that accepts the write.
        _issuedRequests.Clear();

        JobRun secondRun = BuildJobRun(connection);
        SetupRun(secondRun);
        UseStateStore();

        UseSource((request, call) => Task.FromResult(call <= 3
            ? new OrdersPage { Orders = BuildOrdersForDay(request.DateFrom), HasMore = false }
            : new OrdersPage { Orders = new List<ChannelOrderSnapshot>(), Throttled = true, RetryAt = _timeProvider.GetUtcNow().AddHours(3) }));

        ProcessOrdersImportConsumer secondConsumer = CreateConsumer();

        await RunAsync(secondRun, connection, secondConsumer);

        // The failed run left the cursor untouched, so the next run re-fetches the same days. Order
        // persistence upserts by (ChannelId, ExternalOrderId), so the repeat is idempotent and the
        // final state matches what the first run would have produced had it persisted.
        List<string> secondRunOrderIds = PublishedOrderIds()
            .Skip(firstRunOrderIds.Count)
            .ToList();

        secondRunOrderIds.Take(firstRunOrderIds.Count).Should().Equal(firstRunOrderIds);
        secondRunOrderIds.Should().HaveCount(6);

        _persistedState.Should().NotBeNull();
        _persistedState!.IsCompleted.Should().BeFalse();
        _persistedState.NextDayToFetch.Should().Be(_issuedRequests[0].DateFrom.AddDays(-3));
    }

    [Fact]
    public async Task ProcessAsync_NinetyDaysAcrossThrottledRuns_RefetchesWalkStartDuringCatchUp()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);

        SetupRun(BuildJobRun(connection));
        UseStateStore();

        const int daysPerRun = 30;

        // 91 days at 30 per run: the first three runs are throttled short of the boundary and the
        // fourth reaches it, which is the shape a real key produces once its window closes.
        int runsNeeded = (GetDayCountInScope() / daysPerRun) + 1;

        List<string> allPublished = new List<string>();

        for (int run = 0; run < runsNeeded; run++)
        {
            _issuedRequests.Clear();

            JobRun jobRun = BuildJobRun(connection);
            SetupRun(jobRun);

            // Each run gets its own budget of days and is then throttled, which is what a real key
            // does once its window closes. The throttle is what ends the run, not the scope.
            UseSource((request, call) => Task.FromResult(call <= daysPerRun
                ? new OrdersPage { Orders = BuildOrdersForDay(request.DateFrom), HasMore = false }
                : new OrdersPage { Orders = new List<ChannelOrderSnapshot>(), Throttled = true, RetryAt = _timeProvider.GetUtcNow().AddHours(3) }));

            ProcessOrdersImportConsumer consumer = CreateConsumer();

            await RunAsync(jobRun, connection, consumer);

            allPublished.AddRange(PublishedOrderIds().Skip(allPublished.Count));
        }

        int expectedDays = GetDayCountInScope();

        _persistedState.Should().NotBeNull();
        _persistedState!.IsCompleted.Should().BeTrue();

        allPublished.Should().HaveCount((expectedDays + 1) * 2);
        allPublished.Distinct().Should().HaveCount(expectedDays * 2, "the backwards walk covers each day once and catch-up re-fetches the walk start day");
    }

    [Fact]
    public async Task ProcessAsync_CompletedCursor_TakesDeltaBranchInsteadOfWalking()
    {
        SystemChannelTemplate template = BuildWildberriesTemplate();
        Connection connection = BuildConnection(template);
        JobRun jobRun = BuildJobRun(connection);
        DateTimeOffset lastChangedAt = _timeProvider.GetUtcNow().AddHours(-2);

        SetupRun(jobRun);

        _persistedState = new OrdersBackfillState
        {
            Id = Guid.NewGuid(),
            ConnectionId = connection.Id,
            NextDayToFetch = _timeProvider.GetUtcNow().AddDays(-91),
            OldestDayInScope = _timeProvider.GetUtcNow().AddDays(-90),
            IsCompleted = true,
            CompletedAt = _timeProvider.GetUtcNow().AddDays(-1),
        };

        UseStateStore();

        _syncCheckpointRepo
            .Setup(r => r.GetAsync(connection.Id, JobType.OrdersImport, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncCheckpoint
            {
                Id = Guid.NewGuid(),
                ConnectionId = connection.Id,
                JobType = JobType.OrdersImport,
                LastChangedAt = lastChangedAt,
            });

        UseSource((_, _) => Task.FromResult(new OrdersPage
        {
            Orders = new List<ChannelOrderSnapshot>(),
            HasMore = false,
        }));

        ProcessOrdersImportConsumer consumer = CreateConsumer();

        await RunAsync(jobRun, connection, consumer);

        _issuedRequests.Should().ContainSingle();
        _issuedRequests[0].Flag.Should().Be(0);
        _issuedRequests[0].DateFrom.Should().Be(
            lastChangedAt.Subtract(ProcessOrdersImportConsumer.SafetyOverlap).ToOffset(MoscowOffset));
    }
}
