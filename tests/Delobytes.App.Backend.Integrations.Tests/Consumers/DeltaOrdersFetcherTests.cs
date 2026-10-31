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
/// Tests for the delta branch on its own: the window it derives from the watermark, and the two
/// things a stop means there - nothing already read is published, and the run earns no new
/// watermark.
/// </summary>
public class DeltaOrdersFetcherTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 21, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _timeProvider = new FakeTimeProvider(Now);
    private readonly Mock<IJobCheckpointRepository> _jobCheckpointRepo = new();
    private readonly Mock<ISyncCheckpointRepository> _syncCheckpointRepo = new();
    private readonly Mock<IEventPublisher> _eventPublisher = new();
    private readonly Mock<ILogger<BisectingOrdersReader>> _bisectingReaderLogger = new();
    private readonly Mock<ILogger<DeltaOrdersFetcher>> _fetcherLogger = new();
    private readonly Mock<IOrdersChannelClient> _ordersSource = new();

    private readonly List<OrdersRequest> _issuedRequests = new();
    private readonly List<OrdersBatchImportRequestedEvent> _publishedBatches = new();

    public interface IOrdersChannelClient : IChannelApiClient, IOrdersSource
    {
    }

    private DeltaOrdersFetcher CreateFetcher(
        int maxOrdersPerResponse = 80000,
        TimeSpan? minOrdersWindow = null,
        int maxInProcessWaitSeconds = 300)
    {
        WildberriesImportOptions options = new WildberriesImportOptions
        {
            MaxOrdersPerResponse = maxOrdersPerResponse,
            MinOrdersWindow = minOrdersWindow ?? TimeSpan.FromHours(1),
            MaxInProcessWaitSeconds = maxInProcessWaitSeconds,
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

        BisectingOrdersReader bisectingReader = new BisectingOrdersReader(
            pacedSource,
            _bisectingReaderLogger.Object,
            wrappedOptions);

        _eventPublisher
            .Setup(p => p.PublishAsync(It.IsAny<OrdersBatchImportRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<OrdersBatchImportRequestedEvent, CancellationToken>((e, _) => _publishedBatches.Add(e))
            .Returns(Task.CompletedTask);

        return new DeltaOrdersFetcher(
            bisectingReader,
            _syncCheckpointRepo.Object,
            batchPublisher,
            _fetcherLogger.Object);
    }

    private static JobRun BuildJobRun()
    {
        JobRun jobRun = new JobRun
        {
            Id = Guid.NewGuid(),
            ConnectionId = Guid.NewGuid(),
            Status = JobStatus.Running,
        };

        return jobRun;
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

    private static OrdersBackfillState BuildCompletedCursor(Guid connectionId, DateTimeOffset handoverWatermark)
    {
        DateTimeOffset todayStart = OrdersWindowPlanner.GetMoscowDayStart(Now);

        return new OrdersBackfillState
        {
            Id = Guid.NewGuid(),
            ConnectionId = connectionId,
            NextDayToFetch = todayStart,
            OldestDayInScope = todayStart - OrdersImportConstants.BackfillWindow,
            IsCompleted = true,
            HandoverWatermark = handoverWatermark,
            CompletedAt = Now,
        };
    }

    private static ChannelOrderSnapshot BuildOrder(string externalOrderId, DateTimeOffset? statusChangedAt)
    {
        return new ChannelOrderSnapshot
        {
            ExternalOrderId = externalOrderId,
            ExternalOrderNumber = externalOrderId,
            OrderDate = Now,
            StatusChangedAt = statusChangedAt,
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

    private (OrdersImportContext Context, OrdersRunProgress Progress) BuildRun(
        JobRun jobRun,
        Connection connection,
        OrdersBackfillState cursor,
        DateTimeOffset windowTo)
    {
        var context = new OrdersImportContext(
            jobRun,
            connection,
            _ordersSource.Object,
            cursor,
            _timeProvider.GetUtcNow().Add(OrdersImportConstants.BackfillWindow),
            windowTo,
            false);

        var progress = new OrdersRunProgress(0);

        return (context, progress);
    }

    [Fact]
    public async Task FetchAsync_NoSyncCheckpoint_StartsAtTheHandoverWatermark()
    {
        JobRun jobRun = BuildJobRun();
        Connection connection = BuildConnection(jobRun);
        DateTimeOffset handover = Now.AddHours(-3);
        OrdersBackfillState cursor = BuildCompletedCursor(connection.Id, handover);
        (OrdersImportContext context, OrdersRunProgress progress) = BuildRun(jobRun, connection, cursor, Now);

        _syncCheckpointRepo
            .Setup(r => r.GetAsync(connection.Id, JobType.OrdersImport, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncCheckpoint?)null);

        UseOrdersSource(_ => new OrdersPage { Orders = new List<ChannelOrderSnapshot> { BuildOrder("ORD-1", Now.AddMinutes(-30)) } });

        FetchOutcome outcome = await CreateFetcher().FetchAsync(context, progress, CancellationToken.None);

        outcome.StopReason.Should().Be(StopReason.Completed);
        _issuedRequests.Should().ContainSingle();
        _issuedRequests[0].Selection.Should().Be(OrdersSelectionMode.LastChanged);
        _issuedRequests[0].DateFrom.Should().Be(handover.ToOffset(OrdersWindowPlanner.MoscowOffset));
        _issuedRequests[0].DateTo.Should().Be(Now.ToOffset(OrdersWindowPlanner.MoscowOffset));
    }

    [Fact]
    public async Task FetchAsync_ThrottledWindow_PublishesNothingAndReturnsTheStop()
    {
        JobRun jobRun = BuildJobRun();
        Connection connection = BuildConnection(jobRun);
        OrdersBackfillState cursor = BuildCompletedCursor(connection.Id, Now.AddHours(-3));
        (OrdersImportContext context, OrdersRunProgress progress) = BuildRun(jobRun, connection, cursor, Now);
        DateTimeOffset retryAt = Now.AddMinutes(5);

        _syncCheckpointRepo
            .Setup(r => r.GetAsync(connection.Id, JobType.OrdersImport, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncCheckpoint?)null);

        UseOrdersSource(_ => new OrdersPage { Throttled = true, RetryAt = retryAt });

        // A max in-process wait of zero makes any throttle an immediate stop instead of a wait.
        FetchOutcome outcome = await CreateFetcher(maxInProcessWaitSeconds: 0).FetchAsync(context, progress, CancellationToken.None);

        outcome.StopReason.Should().Be(StopReason.Throttled);
        outcome.ResumeAt.Should().Be(retryAt);
        outcome.IsStopped.Should().BeTrue();
        outcome.Orders.Should().BeEmpty();
        outcome.MaxChangedAt.Should().BeNull();
        _publishedBatches.Should().BeEmpty();
    }

    [Fact]
    public async Task FetchAsync_CompleteWindow_PublishesOneUnitAndReportsNewestObservedChange()
    {
        JobRun jobRun = BuildJobRun();
        Connection connection = BuildConnection(jobRun);
        OrdersBackfillState cursor = BuildCompletedCursor(connection.Id, Now.AddHours(-3));
        (OrdersImportContext context, OrdersRunProgress progress) = BuildRun(jobRun, connection, cursor, Now);
        DateTimeOffset newest = Now.AddMinutes(-10);

        _syncCheckpointRepo
            .Setup(r => r.GetAsync(connection.Id, JobType.OrdersImport, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncCheckpoint?)null);

        UseOrdersSource(_ => new OrdersPage
        {
            Orders = new List<ChannelOrderSnapshot>
            {
                BuildOrder("ORD-1", Now.AddMinutes(-40)),
                BuildOrder("ORD-2", newest),
            },
        });

        FetchOutcome outcome = await CreateFetcher().FetchAsync(context, progress, CancellationToken.None);

        outcome.StopReason.Should().Be(StopReason.Completed);
        outcome.OrdersFetched.Should().Be(2);
        outcome.MaxChangedAt.Should().Be(newest.ToUniversalTime());
        outcome.HandoverWatermark.Should().BeNull();
        outcome.IsBackfillCompleted.Should().BeFalse();
        _publishedBatches.Should().ContainSingle();
        _publishedBatches[0].Orders.Should().HaveCount(2);
    }

    [Fact]
    public async Task FetchAsync_ObservedChangeBeyondWindowEnd_ClampsMaxChangedAtToWindowEnd()
    {
        JobRun jobRun = BuildJobRun();
        Connection connection = BuildConnection(jobRun);
        OrdersBackfillState cursor = BuildCompletedCursor(connection.Id, Now.AddHours(-3));
        DateTimeOffset windowTo = Now.AddMinutes(-20);
        (OrdersImportContext context, OrdersRunProgress progress) = BuildRun(jobRun, connection, cursor, windowTo);

        _syncCheckpointRepo
            .Setup(r => r.GetAsync(connection.Id, JobType.OrdersImport, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncCheckpoint?)null);

        UseOrdersSource(_ => new OrdersPage
        {
            Orders = new List<ChannelOrderSnapshot> { BuildOrder("ORD-1", Now.AddMinutes(-1)) },
        });

        FetchOutcome outcome = await CreateFetcher().FetchAsync(context, progress, CancellationToken.None);

        outcome.MaxChangedAt.Should().Be(windowTo);
    }

    [Fact]
    public async Task FetchAsync_EmptyWindow_LeavesMaxChangedAtNull()
    {
        JobRun jobRun = BuildJobRun();
        Connection connection = BuildConnection(jobRun);
        OrdersBackfillState cursor = BuildCompletedCursor(connection.Id, Now.AddHours(-3));
        (OrdersImportContext context, OrdersRunProgress progress) = BuildRun(jobRun, connection, cursor, Now);

        _syncCheckpointRepo
            .Setup(r => r.GetAsync(connection.Id, JobType.OrdersImport, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncCheckpoint?)null);

        UseOrdersSource(_ => new OrdersPage());

        FetchOutcome outcome = await CreateFetcher().FetchAsync(context, progress, CancellationToken.None);

        outcome.StopReason.Should().Be(StopReason.Completed);
        outcome.OrdersFetched.Should().Be(0);
        outcome.MaxChangedAt.Should().BeNull();
        _publishedBatches.Should().BeEmpty();
    }

    [Fact]
    public async Task FetchAsync_RunWithoutResolvedSource_Throws()
    {
        JobRun jobRun = BuildJobRun();
        Connection connection = BuildConnection(jobRun);
        OrdersBackfillState cursor = BuildCompletedCursor(connection.Id, Now.AddHours(-3));

        Func<Task> act = () => CreateFetcher().FetchAsync(
            new OrdersImportContext(
                jobRun,
                connection,
                null!,
                cursor,
                _timeProvider.GetUtcNow().Add(OrdersImportConstants.BackfillWindow),
                Now,
                false),
            new OrdersRunProgress(0),
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
