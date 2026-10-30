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

public partial class ProcessOrdersImportConsumerBackfillTests
{
    private void CapturePayload(Action<OrdersImportCheckpointPayload> onCaptured)
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

    [Fact]
    public async Task ProcessAsync_PublishFailureOnSecondDay_KeepsCursorAndCreatesNoContinuation()
    {
        Connection connection = BuildConnection(BuildWildberriesTemplate());
        JobRun run = BuildJobRun(connection);
        SetupRun(run);
        UseStateStore();
        UseInstantCompleteSource();
        int published = 0;
        _eventPublisher.Setup(p => p.PublishAsync(It.IsAny<OrdersBatchImportRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Returns((OrdersBatchImportRequestedEvent batch, CancellationToken token) =>
            {
                published++;
                return published == 2 ? Task.FromException(new InvalidOperationException("publish failed")) : Task.CompletedTask;
            });

        await RunAsync(run, connection, CreateConsumer());

        run.Status.Should().Be(JobStatus.Failed);
        published.Should().Be(2);
        _persistedState!.NextDayToFetch.Should().Be(GetMoscowDayStart(_timeProvider.GetUtcNow()).AddDays(-1));
        _jobRunRepo.Verify(r => r.CreateAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_ContinuationExists_RedeliveryDoesNotCreateAnother()
    {
        Connection connection = BuildConnection(BuildWildberriesTemplate());
        JobRun run = BuildJobRun(connection);
        SetupRun(run);
        UseStateStore();
        UseSource((request, call) => Task.FromResult(new OrdersPage { Throttled = true, RetryAt = _timeProvider.GetUtcNow().AddHours(3) }));
        bool pending = false;
        _jobRunRepo.Setup(r => r.HasPendingJobAsync(connection.Id, JobType.OrdersImport, run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(() => pending);
        _jobRunRepo.Setup(r => r.CreateAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()))
            .Callback<JobRun, CancellationToken>((created, token) => pending = true)
            .ReturnsAsync((JobRun created, CancellationToken token) => created);

        await RunAsync(run, connection, CreateConsumer());
        await RunAsync(run, connection, CreateConsumer());

        _jobRunRepo.Verify(r => r.CreateAsync(It.Is<JobRun>(j => j.ConnectionId == connection.Id && j.JobType == JobType.OrdersImport && j.Status == JobStatus.Pending && j.NotBefore == _timeProvider.GetUtcNow().AddHours(3)), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_CursorOutsideRetention_SkipsExpiredDaysAndWarnsWithCount()
    {
        const int overshootDays = 5;

        Connection connection = BuildConnection(BuildWildberriesTemplate());
        JobRun run = BuildJobRun(connection);
        DateTimeOffset today = GetMoscowDayStart(_timeProvider.GetUtcNow());
        DateTimeOffset retentionStart = today.AddDays(-(int)ProcessOrdersImportConsumer.BackfillWindow.TotalDays);
        SetupRun(run);
        _persistedState = new OrdersBackfillState
        {
            Id = Guid.NewGuid(),
            ConnectionId = connection.Id,
            OldestDayInScope = retentionStart.AddDays(-overshootDays),
            NextDayToFetch = retentionStart.AddDays(-1),
        };
        UseStateStore();
        UseInstantCompleteSource();

        await RunAsync(run, connection, CreateConsumer());

        _issuedRequests.Should().NotBeEmpty();
        _issuedRequests.Should().OnlyContain(q => q.DateFrom >= retentionStart && q.Flag == 1);
        _issuedRequests[0].DateFrom.Should().Be(today.AddDays(-overshootDays));
        _persistedState!.IsCompleted.Should().BeTrue();
        _backfillWalkerLogger.Verify(l => l.Log(LogLevel.Warning, It.IsAny<EventId>(), It.Is<It.IsAnyType>((value, type) => value.ToString()!.Contains($"skipped {overshootDays} backfill day(s) outside retention")), It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_ThrottledCatchUp_PersistsDayAndDefersContinuation()
    {
        Connection connection = BuildConnection(BuildWildberriesTemplate());
        JobRun run = BuildJobRun(connection);
        DateTimeOffset today = GetMoscowDayStart(_timeProvider.GetUtcNow());
        DateTimeOffset retryAt = _timeProvider.GetUtcNow().AddHours(3);
        SetupRun(run);
        _persistedState = new OrdersBackfillState
        {
            Id = Guid.NewGuid(), ConnectionId = connection.Id,
            OldestDayInScope = today.AddDays(-90), NextDayToFetch = today.AddDays(-91), CatchUpNextDay = today.AddDays(-1),
        };
        UseStateStore();
        UseSource((request, call) => Task.FromResult(call == 1
            ? new OrdersPage { Orders = BuildOrdersForDay(request.DateFrom) }
            : new OrdersPage { Throttled = true, RetryAt = retryAt }));

        await RunAsync(run, connection, CreateConsumer());

        _issuedRequests[0].DateFrom.Should().Be(today.AddDays(-1));
        _issuedRequests[1].DateFrom.Should().Be(today);
        _persistedState!.CatchUpNextDay.Should().Be(today);
        _persistedState.IsCompleted.Should().BeFalse();
        _persistedState.HandoverWatermark.Should().BeNull();
        _jobRunRepo.Verify(r => r.CreateAsync(It.Is<JobRun>(j => j.NotBefore == retryAt), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_ResumedBatchNumbering_StoppedRunPersistsProgressAndStaysUnfinalised()
    {
        Connection connection = BuildConnection(BuildWildberriesTemplate());
        JobRun run = BuildJobRun(connection);
        SetupRun(run);
        UseStateStore();
        _jobCheckpointRepo.Setup(r => r.GetByJobRunIdAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new JobCheckpoint
        {
            JobRunId = run.Id,
            Payload = JsonSerializer.Serialize(new OrdersImportCheckpointPayload { IsBackfill = true, IsFetchCompleted = false, TotalBatches = 7 }),
        });
        UseSource((request, call) => Task.FromResult(call == 1
            ? new OrdersPage { Orders = BuildOrdersForDay(request.DateFrom, OrdersImportConstants.BatchSize + 1) }
            : new OrdersPage { Throttled = true, RetryAt = _timeProvider.GetUtcNow().AddHours(3) }));
        List<OrdersBatchImportRequestedEvent> batches = new List<OrdersBatchImportRequestedEvent>();
        OrdersImportCheckpointPayload? latest = null;
        CapturePayload(payload => latest = payload);
        _eventPublisher.Setup(p => p.PublishAsync(It.IsAny<OrdersBatchImportRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<OrdersBatchImportRequestedEvent, CancellationToken>((batch, token) =>
            {
                batch.IsTerminal.Should().BeFalse("a stopped run has not finished fetching and may not finalise the job");
                batches.Add(batch);
            }).Returns(Task.CompletedTask);

        await RunAsync(run, connection, CreateConsumer());

        // Batch numbering resumes at the persisted count, so the two data batches carry 7 and 8 and
        // a later run continues at 9 instead of reusing BatchIds the aggregator has recorded.
        batches.Should().HaveCount(2);
        batches[0].Orders.Should().HaveCount(OrdersImportConstants.BatchSize);
        batches[1].Orders.Should().HaveCount(1);
        batches.Select(b => b.BatchId).Should().Equal(
            ProcessOrdersImportConsumer.CreateBatchId(run.Id, 7),
            ProcessOrdersImportConsumer.CreateBatchId(run.Id, 8));

        latest.Should().NotBeNull();
        latest!.IsFetchCompleted.Should().BeFalse();
        latest.TotalBatches.Should().Be(9);
        latest.IsBackfillCompleted.Should().BeFalse();

        // Two writes carry this state: the progress write that follows the published unit, and the
        // stop write that adds PagesFetched. Neither finalises the run.
        _jobCheckpointRepo.Verify(
            r => r.UpsertAsync(
                It.Is<JobCheckpoint>(c => c.Payload.Contains("\"TotalBatches\":9") && c.Payload.Contains("\"IsFetchCompleted\":false")),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));

        run.Status.Should().NotBe(JobStatus.Failed);
        _jobRunRepo.Verify(r => r.CreateAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_EmptyDays_PublishOnlyTerminalBatch()
    {
        Connection connection = BuildConnection(BuildWildberriesTemplate());
        JobRun run = BuildJobRun(connection);
        SetupRun(run);
        UseStateStore();
        UseSource((request, call) => Task.FromResult(new OrdersPage()));
        List<OrdersImportCheckpointPayload> payloads = new List<OrdersImportCheckpointPayload>();
        CapturePayload(payloads.Add);

        await RunAsync(run, connection, CreateConsumer());

        _eventPublisher.Verify(p => p.PublishAsync(It.Is<OrdersBatchImportRequestedEvent>(b => b.IsTerminal && b.Orders.Count == 0), It.IsAny<CancellationToken>()), Times.Once);
        _eventPublisher.Verify(p => p.PublishAsync(It.Is<OrdersBatchImportRequestedEvent>(b => !b.IsTerminal), It.IsAny<CancellationToken>()), Times.Never);
        payloads.Last().TotalBatches.Should().Be(1);
        payloads.Last().IsFetchCompleted.Should().BeTrue();
    }

    [Theory]
    [InlineData(301, 1200)]
    [InlineData(60, 60)]
    [InlineData(61, 60)]
    public async Task ProcessAsync_RetryExceedsWaitOrReachesDeadline_DefersWithoutWaiting(int retrySeconds, int budgetSeconds)
    {
        Connection connection = BuildConnection(BuildWildberriesTemplate());
        JobRun run = BuildJobRun(connection);
        SetupRun(run);
        UseStateStore();
        DateTimeOffset now = _timeProvider.GetUtcNow();
        DateTimeOffset retryAt = now.AddSeconds(retrySeconds);
        UseSource((request, call) => Task.FromResult(new OrdersPage { Throttled = true, RetryAt = retryAt }));

        await RunAsync(run, connection, CreateConsumer(backfillRunBudgetSeconds: budgetSeconds));

        _timeProvider.GetUtcNow().Should().Be(now);
        _issuedRequests.Should().ContainSingle();
        _persistedState!.NextDayToFetch.Should().Be(GetMoscowDayStart(now));
        _jobRunRepo.Verify(r => r.CreateAsync(It.Is<JobRun>(j => j.NotBefore == retryAt), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_ThrottledTodayCatchUp_RefetchesTodayOnNextRunAndUsesRequestStartWatermark()
    {
        Connection connection = BuildConnection(BuildWildberriesTemplate());
        JobRun firstRun = BuildJobRun(connection);
        DateTimeOffset today = GetMoscowDayStart(_timeProvider.GetUtcNow());
        SetupRun(firstRun);
        _persistedState = new OrdersBackfillState
        {
            Id = Guid.NewGuid(), ConnectionId = connection.Id,
            OldestDayInScope = today.AddDays(-90), NextDayToFetch = today.AddDays(-91), CatchUpNextDay = today,
        };
        UseStateStore();
        UseSource((request, call) => Task.FromResult(new OrdersPage { Throttled = true, RetryAt = _timeProvider.GetUtcNow().AddHours(3) }));
        await RunAsync(firstRun, connection, CreateConsumer());
        _persistedState!.CatchUpNextDay.Should().Be(today);
        _persistedState.IsCompleted.Should().BeFalse();
        _timeProvider.Advance(TimeSpan.FromHours(3));
        DateTimeOffset requestStartedAt = _timeProvider.GetUtcNow();
        JobRun secondRun = BuildJobRun(connection);
        SetupRun(secondRun);
        UseSource((request, call) =>
        {
            _timeProvider.Advance(TimeSpan.FromMinutes(5));
            return Task.FromResult(new OrdersPage { Orders = BuildOrdersForDay(request.DateFrom) });
        });
        OrdersImportCheckpointPayload? final = null;
        CapturePayload(payload => final = payload);
        _eventPublisher.Setup(p => p.PublishAsync(It.IsAny<OrdersBatchImportRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<OrdersBatchImportRequestedEvent, CancellationToken>((batch, token) =>
            {
                if (batch.IsTerminal)
                {
                    _persistedState!.IsCompleted.Should().BeTrue();
                    _persistedState.HandoverWatermark.Should().Be(requestStartedAt - ProcessOrdersImportConsumer.SafetyOverlap);
                    final!.IsFetchCompleted.Should().BeTrue();
                    final.HandoverWatermark.Should().Be(_persistedState.HandoverWatermark);
                }
            }).Returns(Task.CompletedTask);

        await RunAsync(secondRun, connection, CreateConsumer());

        _issuedRequests.Should().HaveCount(2);
        _issuedRequests.Should().OnlyContain(q => q.Flag == 1 && q.DateFrom == today);
        _persistedState.IsCompleted.Should().BeTrue();
        _persistedState.HandoverWatermark.Should().Be(requestStartedAt - ProcessOrdersImportConsumer.SafetyOverlap);
        final!.HandoverWatermark.Should().Be(_persistedState.HandoverWatermark);
        final.IsBackfillCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task ProcessAsync_CatchUpCrossesMidnight_ContinuesForwardWithoutRewind()
    {
        Connection connection = BuildConnection(BuildWildberriesTemplate());
        JobRun firstRun = BuildJobRun(connection);
        DateTimeOffset today = GetMoscowDayStart(_timeProvider.GetUtcNow());

        SetupRun(firstRun);

        _persistedState = new OrdersBackfillState
        {
            Id = Guid.NewGuid(),
            ConnectionId = connection.Id,
            OldestDayInScope = today.AddDays(-90),
            NextDayToFetch = today.AddDays(-91),
            CatchUpNextDay = today.AddDays(-1),
        };

        UseStateStore();

        JobRun? continuation = null;

        _jobRunRepo
            .Setup(r => r.CreateAsync(
                It.IsAny<JobRun>(),
                It.IsAny<CancellationToken>()))
            .Callback<JobRun, CancellationToken>((created, token) =>
            {
                continuation = created;
            })
            .ReturnsAsync((JobRun created, CancellationToken token) => created);

        UseSource((request, call) =>
        {
            _timeProvider.Advance(TimeSpan.FromDays(1));
            return Task.FromResult(new OrdersPage());
        });

        await RunAsync(
            firstRun,
            connection,
            CreateConsumer(backfillRunBudgetSeconds: 1200));

        _issuedRequests.Select(q => q.DateFrom)
            .Should().Equal(today.AddDays(-1));

        _persistedState.Should().NotBeNull();
        _persistedState!.CatchUpNextDay.Should().Be(today);
        _persistedState.IsCompleted.Should().BeFalse();
        _persistedState.HandoverWatermark.Should().BeNull();
        firstRun.Status.Should().Be(JobStatus.Running);

        continuation.Should().NotBeNull();
        continuation!.ConnectionId.Should().Be(connection.Id);
        continuation.JobType.Should().Be(JobType.OrdersImport);
        continuation.Status.Should().Be(JobStatus.Pending);
        continuation.NotBefore.Should().NotBeNull();

        JobRun secondRun = continuation;
        secondRun.Connection = connection;

        SetupRun(secondRun);
        UseSource((request, call) => Task.FromResult(new OrdersPage()));

        DateTimeOffset secondRunStartedAt = _timeProvider.GetUtcNow();

        await RunAsync(
            secondRun,
            connection,
            CreateConsumer(backfillRunBudgetSeconds: 1200));

        _issuedRequests.Select(q => q.DateFrom)
            .Should().Equal(today.AddDays(-1), today, today.AddDays(1));

        _issuedRequests.Should().OnlyContain(q => q.Flag == 1);

        _persistedState!.CatchUpNextDay.Should().Be(today.AddDays(1));
        _persistedState.IsCompleted.Should().BeTrue();
        _persistedState.CompletedAt.Should().Be(secondRunStartedAt);
        _persistedState.HandoverWatermark.Should().Be(
            secondRunStartedAt - ProcessOrdersImportConsumer.SafetyOverlap);

        secondRun.Status.Should().Be(JobStatus.Running);

        _jobRunRepo.Verify(
            r => r.CreateAsync(
                It.IsAny<JobRun>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
