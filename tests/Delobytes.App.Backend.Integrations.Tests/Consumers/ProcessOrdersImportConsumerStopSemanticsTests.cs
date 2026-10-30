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
using Delobytes.App.Backend.Integrations.Contracts.Events;
using Delobytes.App.Backend.Integrations.Contracts.Models;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers;
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
/// Stage 10c: both branches share one stop outcome. A run halted by a throttle or by the run budget
/// keeps its data incomplete, so it must not finalise the job, must not publish a terminal batch and
/// must not be reported as Failed - it persists progress and hands the retry to a successor run.
/// </summary>
public partial class ProcessOrdersImportConsumerTests
{
    [Fact]
    public async Task ProcessAsync_StoppedDelta_PersistsProgressWithoutFinalising()
    {
        Connection connection = BuildConnection(BuildWildberriesTemplate());
        JobRun run = BuildJobRun(connection);
        SetupDefaults(run);

        _syncCheckpointRepo
            .Setup(r => r.GetAsync(connection.Id, JobType.OrdersImport, It.IsAny<CancellationToken>()))
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

        final.IsFetchCompleted.Should().BeFalse();
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
            .Setup(r => r.GetAsync(connection.Id, JobType.OrdersImport, It.IsAny<CancellationToken>()))
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
    public async Task ProcessAsync_StoppedDelta_LogsStopReasonAndResumeInstant()
    {
        Connection connection = BuildConnection(BuildWildberriesTemplate());
        JobRun run = BuildJobRun(connection);
        SetupDefaults(run);

        _syncCheckpointRepo
            .Setup(r => r.GetAsync(connection.Id, JobType.OrdersImport, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncCheckpoint?)null);

        _ordersSource
            .Setup(s => s.GetOrdersAsync(connection.Id, It.IsAny<OrdersRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrdersPage { Throttled = true, RetryAt = _timeProvider.GetUtcNow().AddHours(3) });

        await CreateConsumer().ProcessAsync(
            new OrdersImportRequestedEvent { JobRunId = run.Id, ConnectionId = connection.Id },
            CancellationToken.None);

        // The enum name renders as the same word the old string carried, so existing operators'
        // searches keep matching.
        _logger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((value, _) => value.ToString()!.Contains("stopReason=Throttled")),
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
            .Setup(r => r.GetAsync(connection.Id, JobType.OrdersImport, It.IsAny<CancellationToken>()))
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
                It.Is<JobCheckpoint>(c => c.Payload.Contains("\"IsFetchCompleted\":true")),
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
            .Setup(r => r.GetAsync(connection.Id, JobType.OrdersImport, It.IsAny<CancellationToken>()))
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
        payloads.Last().IsFetchCompleted.Should().BeFalse();
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
            .Setup(r => r.GetAsync(connection.Id, JobType.OrdersImport, It.IsAny<CancellationToken>()))
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
}
