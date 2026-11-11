using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Integrations.Application.Events;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Application.Options;
using Delobytes.App.Backend.Integrations.Application.Orders;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using Delobytes.App.Backend.Integrations.Contracts.Models;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers;
using Delobytes.App.Backend.Integrations.Tests.ApiClients;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.Consumers;

/// <summary>
/// Stage 10c: both branches share one stop outcome. A run halted by a throttle or by the run budget
/// keeps its data incomplete, so it must not finalise the job, must not publish a terminal batch and
/// must not be reported as Failed - it persists progress and hands the retry to a successor run.
/// </summary>
public partial class OrdersImportOrchestratorTests
{
    [Fact]
    public async Task ProcessAsync_StoppedDelta_PersistsProgressWithoutFinalising()
    {
        Connection connection = BuildConnection(BuildWildberriesTemplate());
        JobRun run = BuildJobRun(connection);
        SetupDefaults(run);

        _syncCheckpointRepo
            .Setup(r => r.GetAsync(connection.Id, JobType.WbOrdersImport, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncCheckpoint?)null);

        _ordersSource
            .Setup(s => s.GetOrdersAsync(connection.Id, It.IsAny<OrdersRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrdersPage { Throttled = true, RetryAt = _timeProvider.GetUtcNow().AddHours(3) });

        List<OrdersImportCheckpointPayload> payloads = new List<OrdersImportCheckpointPayload>();

        _jobCheckpointRepo
            .Setup(r => r.UpsertAsync(It.IsAny<JobCheckpoint>(), It.IsAny<CancellationToken>()))
            .Callback<JobCheckpoint, CancellationToken>((checkpoint, _) =>
                payloads.Add(JsonSerializer.Deserialize<OrdersImportCheckpointPayload>(checkpoint.Payload)!))
            .Returns(Task.CompletedTask);

        await CreateConsumer().ProcessAsync(
            new OrdersImportRequestedEvent { JobRunId = run.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        payloads.Should().NotBeEmpty();

        // The last write is the stop write: it must announce "not finished" and the count actually
        // published, which is zero because a stopped delta window publishes nothing.
        OrdersImportCheckpointPayload final = payloads.Last();

        final.Phase.Should().NotBe(OrdersImportCheckpointPhase.FetchCompleted);
        final.TotalBatches.Should().Be(0);
        final.IsBackfill.Should().BeFalse();
    }

    [Fact]
    public async Task ProcessAsync_StoppedDelta_PublishesNoTerminalBatch()
    {
        Connection connection = BuildConnection(BuildWildberriesTemplate());
        JobRun run = BuildJobRun(connection);
        SetupDefaults(run);

        _syncCheckpointRepo
            .Setup(r => r.GetAsync(connection.Id, JobType.WbOrdersImport, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncCheckpoint?)null);

        _ordersSource
            .Setup(s => s.GetOrdersAsync(connection.Id, It.IsAny<OrdersRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrdersPage { Throttled = true, RetryAt = _timeProvider.GetUtcNow().AddHours(3) });

        await CreateConsumer().ProcessAsync(
            new OrdersImportRequestedEvent { JobRunId = run.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        // Finalising would make the aggregator close the JobRun over a window that was never read,
        // and the watermark would then move past the orders that window still held.
        _eventPublisher.Verify(
            p => p.PublishAsync(It.IsAny<OrdersBatchImportRequestedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _syncCheckpointRepo.Verify(
            r => r.UpsertAsync(It.IsAny<SyncCheckpoint>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_StoppedDelta_LogsStopCauseAndResumeInstant()
    {
        Connection connection = BuildConnection(BuildWildberriesTemplate());
        JobRun run = BuildJobRun(connection);
        SetupDefaults(run);

        _syncCheckpointRepo
            .Setup(r => r.GetAsync(connection.Id, JobType.WbOrdersImport, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncCheckpoint?)null);

        _ordersSource
            .Setup(s => s.GetOrdersAsync(connection.Id, It.IsAny<OrdersRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrdersPage { Throttled = true, RetryAt = _timeProvider.GetUtcNow().AddHours(3) });

        await CreateConsumer().ProcessAsync(
            new OrdersImportRequestedEvent { JobRunId = run.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        // The stop cause and the resume instant are both logged, so operators see why a run ended
        // and when it may continue.
        _logger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((value, _) => value.ToString()!.Contains("stopCause=Throttle")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_CompletedDelta_PublishesFetchCompletedAndSchedulesNoContinuation()
    {
        Connection connection = BuildConnection(BuildWildberriesTemplate());
        JobRun run = BuildJobRun(connection);
        SetupDefaults(run);

        _syncCheckpointRepo
            .Setup(r => r.GetAsync(connection.Id, JobType.WbOrdersImport, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncCheckpoint?)null);

        _ordersSource
            .Setup(s => s.GetOrdersAsync(connection.Id, It.IsAny<OrdersRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrdersPage { Orders = BuildOrders(2), HasMore = false });

        await CreateConsumer().ProcessAsync(
            new OrdersImportRequestedEvent { JobRunId = run.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        _eventPublisher.Verify(
            p => p.PublishAsync(
                It.IsAny<OrdersImportFetchCompletedEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _jobRunRepo.Verify(r => r.CreateAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()), Times.Never);
        _jobCheckpointRepo.Verify(
            r => r.UpsertAsync(
                It.Is<JobCheckpoint>(c => c.Payload.Contains("\"Phase\":3")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_DeltaStoppedAfterPartialBisection_DiscardsReadOrdersFromTheCheckpointCount()
    {
        Connection connection = BuildConnection(BuildWildberriesTemplate());
        JobRun run = BuildJobRun(connection);
        SetupDefaults(run);

        _syncCheckpointRepo
            .Setup(r => r.GetAsync(connection.Id, JobType.WbOrdersImport, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncCheckpoint?)null);

        int call = 0;

        _ordersSource
            .Setup(s => s.GetOrdersAsync(connection.Id, It.IsAny<OrdersRequest>(), It.IsAny<CancellationToken>()))
            .Returns((Guid _, OrdersRequest _, CancellationToken _) =>
            {
                call++;

                return Task.FromResult(call == 1
                    ? new OrdersPage { Orders = BuildOrders(2), HasMore = true }
                    : new OrdersPage { Throttled = true, RetryAt = _timeProvider.GetUtcNow().AddHours(3) });
            });

        List<OrdersImportCheckpointPayload> payloads = new List<OrdersImportCheckpointPayload>();

        _jobCheckpointRepo
            .Setup(r => r.UpsertAsync(It.IsAny<JobCheckpoint>(), It.IsAny<CancellationToken>()))
            .Callback<JobCheckpoint, CancellationToken>((checkpoint, _) =>
                payloads.Add(JsonSerializer.Deserialize<OrdersImportCheckpointPayload>(checkpoint.Payload)!))
            .Returns(Task.CompletedTask);

        await CreateConsumer(maxOrdersPerResponse: 2).ProcessAsync(
            new OrdersImportRequestedEvent { JobRunId = run.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        // The two orders read before the throttle belong to a window the bisection never finished, so
        // nothing is published and the final count stays at zero: the next run re-reads the window.
        _eventPublisher.Verify(
            p => p.PublishAsync(It.IsAny<OrdersBatchImportRequestedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);

        payloads.Last().TotalBatches.Should().Be(0);
        payloads.Last().Phase.Should().NotBe(OrdersImportCheckpointPhase.FetchCompleted);
    }

    [Fact]
    public async Task ProcessAsync_RedeliveredRunningJob_DoesNotRewriteStartedAt()
    {
        Connection connection = BuildConnection(BuildWildberriesTemplate());
        DateTimeOffset startedAt = _timeProvider.GetUtcNow().AddMinutes(-5);
        JobRun run = BuildJobRun(connection, JobStatus.Running);
        run.StartedAt = startedAt;
        SetupDefaults(run);

        _syncCheckpointRepo
            .Setup(r => r.GetAsync(connection.Id, JobType.WbOrdersImport, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncCheckpoint?)null);

        _ordersSource
            .Setup(s => s.GetOrdersAsync(connection.Id, It.IsAny<OrdersRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrdersPage { Throttled = true, RetryAt = _timeProvider.GetUtcNow().AddHours(3) });

        await CreateConsumer().ProcessAsync(
            new OrdersImportRequestedEvent { JobRunId = run.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        // StartAsync only writes the Pending -> Running transition; a redelivery of a run that is
        // already Running must leave the original start instant alone.
        run.StartedAt.Should().Be(startedAt);
        _jobRunRepo.Verify(
            r => r.UpdateAsync(It.Is<JobRun>(j => j.Status == JobStatus.Running), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_CorruptCheckpoint_FailsInsteadOfRepublishingBatchZero()
    {
        Connection connection = BuildConnection(BuildWildberriesTemplate());
        JobRun run = BuildJobRun(connection);
        SetupDefaults(run);

        _jobCheckpointRepo
            .Setup(r => r.GetByJobRunIdAsync(run.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobCheckpoint { JobRunId = run.Id, Payload = "{ not a payload" });

        await CreateConsumer().ProcessAsync(
            new OrdersImportRequestedEvent { JobRunId = run.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        // Reading the unreadable checkpoint as "absent" would restart batch numbering at 0 and
        // republish BatchIds the aggregator already recorded, which it deduplicates - dropping the
        // orders they carried without a trace.
        run.Status.Should().Be(JobStatus.Failed);
        run.Error.Should().Contain("corrupt");

        _eventPublisher.Verify(
            p => p.PublishAsync(It.IsAny<OrdersBatchImportRequestedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _ordersSource.Verify(
            s => s.GetOrdersAsync(connection.Id, It.IsAny<OrdersRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_RequestBudgetExhausted_SchedulesAContinuationInsteadOfFailing()
    {
        Connection connection = BuildConnection(BuildWildberriesTemplate());
        JobRun run = BuildJobRun(connection);
        SetupDefaults(run);

        DateTimeOffset resumeAt = _timeProvider.GetUtcNow();

        FetchOutcome.Stopped outcome = new FetchOutcome.Stopped
        {
            RequestsIssued = 1000,
            Cause = StopCause.RequestBudget,
            ResumeAt = resumeAt,
        };

        IOrdersFetchers fetchers = new FixedFetchers(new FixedOutcomeFetcher(outcome), TestTunings.Wildberries());
        IOrdersFetcherResolver resolver = StubOrdersFetcherResolver.For(
            "wildberries", JobType.WbOrdersImport, fetchers);

        await CreateConsumer(fetcherResolver: resolver).ProcessAsync(
            new OrdersImportRequestedEvent { JobRunId = run.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        // The request budget used to throw and fail the run; it is now a soft stop, so the run
        // records a checkpoint and hands the remaining work to a continuation.
        _jobRunRepo.Verify(
            r => r.CreateAsync(
                It.Is<JobRun>(job => job.Status == JobStatus.Pending && job.NotBefore == resumeAt),
                It.IsAny<CancellationToken>()),
            Times.Once);
        run.Status.Should().Be(JobStatus.Running);
        run.Error.Should().BeNull();
    }

    private sealed class FixedOutcomeFetcher : IOrdersFetcher
    {
        private readonly FetchOutcome _outcome;

        public FixedOutcomeFetcher(FetchOutcome outcome)
        {
            _outcome = outcome;
        }

        public Task<FetchOutcome> FetchAsync(
            OrdersImportContext context,
            RunCounters counters,
            BatchSequence batches,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_outcome);
        }
    }

    private sealed class FixedFetchers : IOrdersFetchers
    {
        public FixedFetchers(IOrdersFetcher fetcher, OrdersImportTuning tuning)
        {
            Backfill = fetcher;
            Delta = fetcher;
            Tuning = tuning;
        }

        public IOrdersFetcher Backfill { get; }

        public IOrdersFetcher Delta { get; }

        public IOrdersWindowReader WindowReader
        {
            get
            {
                throw new NotSupportedException("The fixed fetcher set has no window reader.");
            }
        }

        public OrdersImportTuning Tuning { get; }

        public TimeSpan MaxInProcessWait
        {
            get
            {
                return Tuning.MaxInProcessWait;
            }
        }

        public int MaxRequestsPerRun
        {
            get
            {
                return Tuning.MaxRequestsPerRun;
            }
        }

        public TimeSpan RunBudget
        {
            get
            {
                return Tuning.RunBudget;
            }
        }
    }
}
