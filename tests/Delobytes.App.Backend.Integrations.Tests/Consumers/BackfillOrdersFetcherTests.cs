using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Application.Options;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using Delobytes.App.Backend.Integrations.Contracts.Models;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers.OrdersImport;
using Delobytes.App.Backend.Integrations.Tests.ApiClients;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.Consumers;

/// <summary>
/// Tests for the backfill branch on its own: the day-per-request walk, and the rule that a cursor
/// which has not completed never earns a delta watermark.
/// </summary>
public class BackfillOrdersFetcherTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 21, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _timeProvider = new FakeTimeProvider(Now);
    private readonly Mock<IJobCheckpointRepository> _jobCheckpointRepo = new();
    private readonly Mock<IOrdersBackfillStateRepository> _backfillStateRepo = new();
    private readonly Mock<IEventPublisher> _eventPublisher = new();
    private readonly Mock<ILogger<OrdersBackfillWalker>> _walkerLogger = new();
    private readonly Mock<IOrdersChannelClient> _ordersSource = new();

    private readonly List<OrdersRequest> _issuedRequests = new();
    private readonly List<OrdersBatchImportRequestedEvent> _publishedBatches = new();

    public interface IOrdersChannelClient : IChannelApiClient, IOrdersSource
    {
    }

    private BackfillOrdersFetcher CreateFetcher()
    {
        WildberriesImportOptions options = new WildberriesImportOptions
        {
            MaxOrdersPerResponse = 80000,
            MinOrdersWindow = TimeSpan.FromHours(1),
            BackfillRunBudgetSeconds = 1200,
        };

        IOptions<WildberriesImportOptions> wrappedOptions = Options.Create(options);

        OrdersImportCheckpointStore checkpointStore = new OrdersImportCheckpointStore(
            _jobCheckpointRepo.Object,
            _timeProvider);

        OrdersBatchPublisher batchPublisher = new OrdersBatchPublisher(
            _eventPublisher.Object,
            checkpointStore);

        PacedOrdersSource pacedSource = new PacedOrdersSource(
            new AlwaysAllowWildberriesRateLimiter(),
            _timeProvider,
            wrappedOptions);

        OrdersBackfillWalker walker = new OrdersBackfillWalker(
            pacedSource,
            _backfillStateRepo.Object,
            batchPublisher,
            _walkerLogger.Object,
            _timeProvider);

        _eventPublisher
            .Setup(p => p.PublishAsync(It.IsAny<OrdersBatchImportRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<OrdersBatchImportRequestedEvent, CancellationToken>((e, _) => _publishedBatches.Add(e))
            .Returns(Task.CompletedTask);

        return new BackfillOrdersFetcher(walker);
    }

    private static JobRun BuildJobRun()
    {
        return new JobRun
        {
            Id = Guid.NewGuid(),
            ConnectionId = Guid.NewGuid(),
            Status = JobStatus.Running,
        };
    }

    private static Connection BuildConnection(JobRun jobRun)
    {
        return new Connection
        {
            Id = jobRun.ConnectionId,
            ChannelId = Guid.NewGuid(),
            ApiKey = "api-key",
            Name = "Connection",
            IsActive = true,
        };
    }

    private static OrdersBackfillState BuildCursor(
        Guid connectionId,
        bool isCompleted,
        DateTimeOffset? handoverWatermark,
        DateTimeOffset? nextDayToFetch = null,
        DateTimeOffset? oldestDayInScope = null)
    {
        DateTimeOffset todayStart = OrdersWindowPlanner.GetMoscowDayStart(Now);

        return new OrdersBackfillState
        {
            Id = Guid.NewGuid(),
            ConnectionId = connectionId,
            NextDayToFetch = nextDayToFetch ?? todayStart,
            OldestDayInScope = oldestDayInScope ?? todayStart - OrdersImportConstants.BackfillWindow,
            IsCompleted = isCompleted,
            HandoverWatermark = handoverWatermark,
            CompletedAt = isCompleted ? Now : null,
        };
    }

    private static ChannelOrderSnapshot BuildOrder(string externalOrderId)
    {
        return new ChannelOrderSnapshot
        {
            ExternalOrderId = externalOrderId,
            ExternalOrderNumber = externalOrderId,
            OrderDate = Now.AddHours(-1),
            StatusChangedAt = Now.AddMinutes(-30),
            ExternalStatus = "new",
            Currency = "RUB",
        };
    }

    private void UseOrdersSource(Func<OrdersRequest, OrdersPage> pageProvider)
    {
        _ordersSource
            .Setup(s => s.GetOrdersAsync(It.IsAny<Guid>(), It.IsAny<OrdersRequest>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, OrdersRequest, CancellationToken>((_, request, _) => _issuedRequests.Add(request))
            .ReturnsAsync((Guid _, OrdersRequest request, CancellationToken _) => pageProvider(request));
    }

    private OrdersImportRun BuildRun(JobRun jobRun, Connection connection, OrdersBackfillState cursor)
    {
        return new OrdersImportRun(
            jobRun,
            connection,
            _timeProvider.GetUtcNow().Add(TimeSpan.FromMinutes(20)),
            Now,
            true,
            nextBatchIndex: 0)
        {
            OrdersSource = _ordersSource.Object,
            BackfillState = cursor,
        };
    }

    [Fact]
    public async Task FetchAsync_CompletedCursor_HandsOverItsStoredWatermarkWithoutReadingAnything()
    {
        JobRun jobRun = BuildJobRun();
        Connection connection = BuildConnection(jobRun);
        DateTimeOffset handover = Now.AddHours(-2);
        OrdersBackfillState cursor = BuildCursor(connection.Id, isCompleted: true, handover);
        OrdersImportRun run = BuildRun(jobRun, connection, cursor);

        FetchOutcome outcome = await CreateFetcher().FetchAsync(run, CancellationToken.None);

        outcome.IsBackfillCompleted.Should().BeTrue();
        outcome.HandoverWatermark.Should().Be(handover);
        outcome.StopReason.Should().Be(StopReason.Completed);
        outcome.MaxChangedAt.Should().BeNull();
        _issuedRequests.Should().BeEmpty();
        _publishedBatches.Should().BeEmpty();
    }

    [Fact]
    public async Task FetchAsync_IncompleteCursor_WalksOneCalendarDayPerRequestWithFlagOne()
    {
        JobRun jobRun = BuildJobRun();
        Connection connection = BuildConnection(jobRun);
        DateTimeOffset todayStart = OrdersWindowPlanner.GetMoscowDayStart(Now);

        // Two days left in the backwards walk, then the catch-up pass walks forward from the top of
        // the scope; with a full 180-day scope that pass lands on today and closes the cursor.
        OrdersBackfillState cursor = BuildCursor(
            connection.Id,
            isCompleted: false,
            handoverWatermark: null,
            nextDayToFetch: todayStart.AddDays(-179),
            oldestDayInScope: todayStart.AddDays(-180));

        OrdersImportRun run = BuildRun(jobRun, connection, cursor);

        UseOrdersSource(_ => new OrdersPage { Orders = new List<ChannelOrderSnapshot> { BuildOrder("ORD-1") } });

        FetchOutcome outcome = await CreateFetcher().FetchAsync(run, CancellationToken.None);

        _issuedRequests.Should().NotBeEmpty();
        _issuedRequests.Should().OnlyContain(request => request.Flag == 1);
        _issuedRequests[0].DateFrom.Should().Be(todayStart.AddDays(-179));
        _issuedRequests[0].DateTo.Should().Be(todayStart.AddDays(-179).AddDays(1).AddSeconds(-1));
        _issuedRequests[1].DateFrom.Should().Be(todayStart.AddDays(-180));

        // The catch-up pass starts where the backwards walk began, so it re-reads today.
        _issuedRequests[2].DateFrom.Should().Be(todayStart);
        _issuedRequests.Should().HaveCount(3);

        outcome.OrdersFetched.Should().Be(_issuedRequests.Count);
        outcome.MaxChangedAt.Should().BeNull();
        _publishedBatches.Should().HaveCount(_issuedRequests.Count);
    }

    [Fact]
    public async Task FetchAsync_StoppedWalk_HandsOverNoWatermarkAndReturnsTheStop()
    {
        JobRun jobRun = BuildJobRun();
        Connection connection = BuildConnection(jobRun);
        DateTimeOffset todayStart = OrdersWindowPlanner.GetMoscowDayStart(Now);

        OrdersBackfillState cursor = BuildCursor(
            connection.Id,
            isCompleted: false,
            handoverWatermark: null,
            nextDayToFetch: todayStart.AddDays(-179),
            oldestDayInScope: todayStart.AddDays(-180));

        // The run is already past its deadline, so the paced source stops before issuing anything.
        OrdersImportRun run = new OrdersImportRun(
            jobRun,
            connection,
            _timeProvider.GetUtcNow(),
            Now,
            true,
            nextBatchIndex: 0)
        {
            OrdersSource = _ordersSource.Object,
            BackfillState = cursor,
        };

        UseOrdersSource(_ => new OrdersPage { Orders = new List<ChannelOrderSnapshot> { BuildOrder("ORD-1") } });

        FetchOutcome outcome = await CreateFetcher().FetchAsync(run, CancellationToken.None);

        outcome.IsStopped.Should().BeTrue();
        outcome.StopReason.Should().Be(StopReason.Budget);
        outcome.IsBackfillCompleted.Should().BeFalse();
        outcome.HandoverWatermark.Should().BeNull();
        outcome.MaxChangedAt.Should().BeNull();
        _publishedBatches.Should().BeEmpty();
    }

    [Fact]
    public async Task FetchAsync_CursorThatCompletesDuringTheRun_HandsOverTheWatermarkStampedByTheCatchUpPass()
    {
        JobRun jobRun = BuildJobRun();
        Connection connection = BuildConnection(jobRun);
        DateTimeOffset todayStart = OrdersWindowPlanner.GetMoscowDayStart(Now);

        OrdersBackfillState cursor = BuildCursor(
            connection.Id,
            isCompleted: false,
            handoverWatermark: null,
            nextDayToFetch: todayStart.AddDays(-179),
            oldestDayInScope: todayStart.AddDays(-180));

        OrdersImportRun run = BuildRun(jobRun, connection, cursor);

        UseOrdersSource(_ => new OrdersPage { Orders = new List<ChannelOrderSnapshot> { BuildOrder("ORD-1") } });

        FetchOutcome outcome = await CreateFetcher().FetchAsync(run, CancellationToken.None);

        // The catch-up pass stamps the watermark from the last request it observed, minus the
        // safety overlap; a fetcher that read the cursor before the walk would report null here.
        outcome.IsBackfillCompleted.Should().BeTrue();
        outcome.HandoverWatermark.Should().Be(Now - OrdersImportConstants.SafetyOverlap);
    }

    [Fact]
    public async Task FetchAsync_RunWithoutBackfillCursor_Throws()
    {
        JobRun jobRun = BuildJobRun();
        Connection connection = BuildConnection(jobRun);
        OrdersImportRun run = BuildRun(jobRun, connection, BuildCursor(connection.Id, isCompleted: true, Now));
        run.BackfillState = null;

        Func<Task> act = () => CreateFetcher().FetchAsync(run, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task FetchAsync_RunWithoutResolvedSource_Throws()
    {
        JobRun jobRun = BuildJobRun();
        Connection connection = BuildConnection(jobRun);
        OrdersImportRun run = BuildRun(jobRun, connection, BuildCursor(connection.Id, isCompleted: true, Now));
        run.OrdersSource = null;

        Func<Task> act = () => CreateFetcher().FetchAsync(run, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
