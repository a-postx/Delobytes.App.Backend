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
    [Theory]
    [InlineData(301)]
    [InlineData(10800)]
    public async Task ProcessAsync_ThrottledDelta_StopsCleanlyAndSchedulesContinuation(int retrySeconds)
    {
        Connection connection = BuildConnection(BuildWildberriesTemplate());
        JobRun run = BuildJobRun(connection);
        SetupDefaults(run);
        DateTimeOffset previous = _timeProvider.GetUtcNow().AddDays(-1);
        DateTimeOffset retryAt = _timeProvider.GetUtcNow().AddSeconds(retrySeconds);
        SyncCheckpoint checkpoint = new SyncCheckpoint { ConnectionId = connection.Id, JobType = JobType.OrdersImport, LastChangedAt = previous };
        _syncCheckpointRepo.Setup(r => r.GetAsync(connection.Id, JobType.OrdersImport, It.IsAny<CancellationToken>())).ReturnsAsync(checkpoint);
        _ordersSource.Setup(s => s.GetOrdersAsync(connection.Id, It.IsAny<OrdersRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new OrdersPage { Throttled = true, RetryAt = retryAt });

        await CreateConsumer().ProcessAsync(new OrdersImportRequestedEvent { JobRunId = run.Id, ConnectionId = connection.Id }, CancellationToken.None);

        // A delta run stopped by a throttle is a scheduling fact, not a failure: it leaves the
        // JobRun active, keeps the watermark untouched and hands the retry to a Pending successor.
        run.Status.Should().NotBe(JobStatus.Failed);
        run.Error.Should().BeNull();
        checkpoint.LastChangedAt.Should().Be(previous);
        _eventPublisher.Verify(p => p.PublishAsync(It.IsAny<OrdersBatchImportRequestedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        _syncCheckpointRepo.Verify(r => r.UpsertAsync(It.IsAny<SyncCheckpoint>(), It.IsAny<CancellationToken>()), Times.Never);
        _jobRunRepo.Verify(r => r.CreateAsync(It.Is<JobRun>(next =>
            next.ConnectionId == connection.Id
            && next.JobType == JobType.OrdersImport
            && next.Status == JobStatus.Pending
            && next.NotBefore == retryAt), It.IsAny<CancellationToken>()), Times.Once);
        _logger.Verify(l => l.Log(LogLevel.Warning, It.IsAny<EventId>(), It.Is<It.IsAnyType>((value, type) => value.ToString()!.Contains("stopped")), It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
        _logger.Verify(l => l.Log(LogLevel.Error, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_ThrottledBisectionSibling_DiscardsAlreadyFetchedOrders()
    {
        Connection connection = BuildConnection(BuildWildberriesTemplate());
        JobRun run = BuildJobRun(connection);
        SetupDefaults(run);
        DateTimeOffset previous = _timeProvider.GetUtcNow().AddDays(-2);
        SyncCheckpoint checkpoint = new SyncCheckpoint { ConnectionId = connection.Id, JobType = JobType.OrdersImport, LastChangedAt = previous };
        _syncCheckpointRepo.Setup(r => r.GetAsync(connection.Id, JobType.OrdersImport, It.IsAny<CancellationToken>())).ReturnsAsync(checkpoint);
        int calls = 0;
        _ordersSource.Setup(s => s.GetOrdersAsync(connection.Id, It.IsAny<OrdersRequest>(), It.IsAny<CancellationToken>()))
            .Returns((Guid id, OrdersRequest request, CancellationToken token) =>
            {
                calls++;
                return Task.FromResult(calls == 1
                    ? new OrdersPage { Orders = BuildOrders(2), HasMore = true }
                    : calls == 2 ? new OrdersPage { Orders = BuildOrders(1, 10) }
                    : new OrdersPage { Throttled = true, RetryAt = _timeProvider.GetUtcNow().AddHours(3) });
            });

        await CreateConsumer(maxOrdersPerResponse: 2).ProcessAsync(new OrdersImportRequestedEvent { JobRunId = run.Id, ConnectionId = connection.Id }, CancellationToken.None);

        calls.Should().Be(3);
        run.Status.Should().NotBe(JobStatus.Failed);
        checkpoint.LastChangedAt.Should().Be(previous);
        _eventPublisher.Verify(p => p.PublishAsync(It.IsAny<OrdersBatchImportRequestedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        _syncCheckpointRepo.Verify(r => r.UpsertAsync(It.IsAny<SyncCheckpoint>(), It.IsAny<CancellationToken>()), Times.Never);
        _jobRunRepo.Verify(r => r.CreateAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(60)]
    [InlineData(300)]
    public async Task ProcessAsync_ShortOrExpiredRetry_RetriesIdenticalDeltaWindow(int seconds)
    {
        Connection connection = BuildConnection(BuildWildberriesTemplate());
        JobRun run = BuildJobRun(connection);
        SetupDefaults(run);
        List<OrdersRequest> requests = new List<OrdersRequest>();
        DateTimeOffset start = _timeProvider.GetUtcNow();
        _ordersSource.Setup(s => s.GetOrdersAsync(connection.Id, It.IsAny<OrdersRequest>(), It.IsAny<CancellationToken>()))
            .Returns((Guid id, OrdersRequest request, CancellationToken token) =>
            {
                requests.Add(request);
                return Task.FromResult(requests.Count == 1
                    ? new OrdersPage { Throttled = true, RetryAt = start.AddSeconds(seconds) }
                    : new OrdersPage { Orders = BuildOrders(1) });
            });
        Task processing = CreateConsumer().ProcessAsync(new OrdersImportRequestedEvent { JobRunId = run.Id, ConnectionId = connection.Id }, CancellationToken.None);
        if (seconds > 0)
        {
            processing.IsCompleted.Should().BeFalse();
            requests.Should().HaveCount(1);
            _timeProvider.Advance(TimeSpan.FromSeconds(seconds));
        }

        await processing;

        requests.Should().HaveCount(2);
        requests[1].DateFrom.Should().Be(requests[0].DateFrom);
        requests[1].DateTo.Should().Be(requests[0].DateTo);
        requests[1].Flag.Should().Be(0);
        run.Status.Should().NotBe(JobStatus.Failed);
        _eventPublisher.Verify(p => p.PublishAsync(It.Is<OrdersBatchImportRequestedEvent>(b => b.Orders.Count == 1), It.IsAny<CancellationToken>()), Times.Once);
        _eventPublisher.Verify(p => p.PublishAsync(It.IsAny<OrdersImportFetchCompletedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProcessAsync_EmptySyncCheckpoint_UsesHandoverOrLegacyWalkStart(bool legacy)
    {
        Connection connection = BuildConnection(BuildWildberriesTemplate());
        JobRun run = BuildJobRun(connection);
        SetupDefaults(run);
        DateTimeOffset oldest = _timeProvider.GetUtcNow().AddDays(-120);
        DateTimeOffset handover = _timeProvider.GetUtcNow().AddDays(-2);
        _backfillStateRepo.Setup(r => r.GetAsync(connection.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new OrdersBackfillState
        {
            ConnectionId = connection.Id, IsCompleted = true, OldestDayInScope = oldest,
            HandoverWatermark = legacy ? null : handover,
        });
        List<OrdersRequest> requests = new List<OrdersRequest>();
        _ordersSource.Setup(s => s.GetOrdersAsync(connection.Id, It.IsAny<OrdersRequest>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, OrdersRequest, CancellationToken>((id, request, token) => requests.Add(request))
            .ReturnsAsync(new OrdersPage());

        await CreateConsumer().ProcessAsync(new OrdersImportRequestedEvent { JobRunId = run.Id, ConnectionId = connection.Id }, CancellationToken.None);

        requests.Should().ContainSingle();
        requests[0].Flag.Should().Be(0);
        requests[0].DateFrom.Should().Be(legacy ? oldest + ProcessOrdersImportConsumer.BackfillWindow : handover);
        _backfillStateRepo.Verify(r => r.UpsertAsync(It.IsAny<OrdersBackfillState>(), It.IsAny<CancellationToken>()), Times.Never);

        if (legacy)
        {
            _deltaFetcherLogger.Verify(l => l.Log(LogLevel.Warning, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.AtLeastOnce);
        }
    }

    [Theory]
    [InlineData(-60)]
    [InlineData(10800)]
    public async Task ProcessAsync_DeltaBudgetExhaustedBeforeSubwindow_UsesLaterOfNowAndLimiterSlot(int slotOffsetSeconds)
    {
        Connection connection = BuildConnection(BuildWildberriesTemplate());
        JobRun run = BuildJobRun(connection);
        SetupDefaults(run);
        DateTimeOffset start = _timeProvider.GetUtcNow();
        DateTimeOffset stoppedAt = start.AddSeconds(1200);
        DateTimeOffset slot = stoppedAt.AddSeconds(slotOffsetSeconds);
        Mock<IWildberriesRateLimiter> limiter = new Mock<IWildberriesRateLimiter>();
        limiter.Setup(l => l.GetNextRequestAllowedAtAsync(connection.ApiKey, WildberriesApiMethod.SupplierOrders, It.IsAny<CancellationToken>())).ReturnsAsync(slot);
        _ordersSource.Setup(s => s.GetOrdersAsync(connection.Id, It.IsAny<OrdersRequest>(), It.IsAny<CancellationToken>()))
            .Returns((Guid id, OrdersRequest request, CancellationToken token) =>
            {
                _timeProvider.Advance(TimeSpan.FromSeconds(1200));
                return Task.FromResult(new OrdersPage { HasMore = true, Orders = BuildOrders(1) });
            });
        ProcessOrdersImportConsumer consumer = CreateConsumer(rateLimiter: limiter.Object);

        await consumer.ProcessAsync(new OrdersImportRequestedEvent { JobRunId = run.Id, ConnectionId = connection.Id }, CancellationToken.None);

        DateTimeOffset expected = slot > stoppedAt ? slot : stoppedAt;

        // A budget-exhausted delta run schedules its retry for the later of "now" and the limiter's
        // next slot, and reports no failure.
        run.Status.Should().NotBe(JobStatus.Failed);
        _ordersSource.Verify(s => s.GetOrdersAsync(connection.Id, It.IsAny<OrdersRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        _eventPublisher.Verify(p => p.PublishAsync(It.IsAny<OrdersBatchImportRequestedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        _syncCheckpointRepo.Verify(s => s.UpsertAsync(It.IsAny<SyncCheckpoint>(), It.IsAny<CancellationToken>()), Times.Never);
        _jobRunRepo.Verify(r => r.CreateAsync(It.Is<JobRun>(next => next.NotBefore == expected), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_ShortWaitWithinBisection_RetriesSubwindowBeforePublishing()
    {
        Connection connection = BuildConnection(BuildWildberriesTemplate());
        JobRun run = BuildJobRun(connection);
        SetupDefaults(run);
        List<OrdersRequest> requests = new List<OrdersRequest>();
        _ordersSource.Setup(s => s.GetOrdersAsync(connection.Id, It.IsAny<OrdersRequest>(), It.IsAny<CancellationToken>()))
            .Returns((Guid id, OrdersRequest request, CancellationToken token) =>
            {
                requests.Add(request);
                return Task.FromResult(requests.Count == 1
                    ? new OrdersPage { HasMore = true, Orders = BuildOrders(1) }
                    : requests.Count == 2 ? new OrdersPage { Throttled = true, RetryAt = _timeProvider.GetUtcNow().AddSeconds(60) }
                    : new OrdersPage());
            });
        Task processing = CreateConsumer().ProcessAsync(new OrdersImportRequestedEvent { JobRunId = run.Id, ConnectionId = connection.Id }, CancellationToken.None);
        processing.IsCompleted.Should().BeFalse();
        requests.Should().HaveCount(2);
        _eventPublisher.Verify(p => p.PublishAsync(It.IsAny<OrdersBatchImportRequestedEvent>(), It.IsAny<CancellationToken>()), Times.Never);

        _timeProvider.Advance(TimeSpan.FromSeconds(60));
        await processing;

        requests.Should().HaveCount(4);
        requests[2].DateFrom.Should().Be(requests[1].DateFrom);
        requests[2].DateTo.Should().Be(requests[1].DateTo);
        run.Status.Should().NotBe(JobStatus.Failed);
        _eventPublisher.Verify(p => p.PublishAsync(It.IsAny<OrdersImportFetchCompletedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_DeltaPublication_PersistsProgressAndFinalCountInOrder()
    {
        Connection connection = BuildConnection(BuildWildberriesTemplate());
        JobRun run = BuildJobRun(connection);
        SetupDefaults(run);
        _ordersSource.Setup(s => s.GetOrdersAsync(connection.Id, It.IsAny<OrdersRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new OrdersPage { Orders = BuildOrders(OrdersImportConstants.BatchSize + 1) });
        List<string> operations = new List<string>();
        _jobCheckpointRepo.Setup(r => r.UpsertAsync(It.IsAny<JobCheckpoint>(), It.IsAny<CancellationToken>()))
            .Callback<JobCheckpoint, CancellationToken>((checkpoint, token) =>
            {
                OrdersImportCheckpointPayload payload = JsonSerializer.Deserialize<OrdersImportCheckpointPayload>(checkpoint.Payload)!;
                operations.Add($"checkpoint:{payload.TotalBatches}:{payload.IsFetchCompleted}");
            }).Returns(Task.CompletedTask);
        _eventPublisher.Setup(p => p.PublishAsync(It.IsAny<OrdersBatchImportRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<OrdersBatchImportRequestedEvent, CancellationToken>((batch, token) => operations.Add($"data:{batch.Orders.Count}"))
            .Returns(Task.CompletedTask);
        _eventPublisher.Setup(p => p.PublishAsync(It.IsAny<OrdersImportFetchCompletedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<OrdersImportFetchCompletedEvent, CancellationToken>((completed, token) => operations.Add($"completed:{completed.TotalBatches}"))
            .Returns(Task.CompletedTask);

        await CreateConsumer().ProcessAsync(new OrdersImportRequestedEvent { JobRunId = run.Id, ConnectionId = connection.Id }, CancellationToken.None);

        operations.Should().Equal(
            "checkpoint:0:False",
            "data:30",
            "data:1",
            "checkpoint:2:False",
            "checkpoint:2:True",
            "completed:2");
    }

    [Fact]
    public async Task ProcessAsync_CancelledDuringFakeTimeWait_DoesNotPublishOrCreateContinuation()
    {
        Connection connection = BuildConnection(BuildWildberriesTemplate());
        JobRun run = BuildJobRun(connection);
        SetupDefaults(run);
        _ordersSource.Setup(s => s.GetOrdersAsync(connection.Id, It.IsAny<OrdersRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrdersPage { Throttled = true, RetryAt = _timeProvider.GetUtcNow().AddSeconds(60) });

        using (CancellationTokenSource cancellation = new CancellationTokenSource())
        {
            Task processing = CreateConsumer().ProcessAsync(new OrdersImportRequestedEvent { JobRunId = run.Id, ConnectionId = connection.Id }, cancellation.Token);
            processing.IsCompleted.Should().BeFalse();
            cancellation.Cancel();
            try
            {
                await processing;
            }
            catch (OperationCanceledException)
            {
            }
        }

        _ordersSource.Verify(s => s.GetOrdersAsync(connection.Id, It.IsAny<OrdersRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        _eventPublisher.Verify(p => p.PublishAsync(It.IsAny<OrdersBatchImportRequestedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        _jobRunRepo.Verify(r => r.CreateAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()), Times.Never);
        _syncCheckpointRepo.Verify(r => r.UpsertAsync(It.IsAny<SyncCheckpoint>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
