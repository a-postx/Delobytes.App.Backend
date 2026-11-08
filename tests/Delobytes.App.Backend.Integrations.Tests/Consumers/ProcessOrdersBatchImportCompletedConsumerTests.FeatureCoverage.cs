using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers;
using Delobytes.App.Backend.Integrations.Application.Orders;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.Consumers;

public partial class ProcessOrdersBatchImportCompletedConsumerTests
{
    private static OrdersImportFetchCompletedEvent BuildFetchCompletedEvent(
        Guid jobRunId,
        int totalBatches,
        DateTimeOffset? maxChangedAt = null,
        bool isBackfill = false)
    {
        return new OrdersImportFetchCompletedEvent
        {
            JobRunId = jobRunId,
            ConnectionId = Guid.NewGuid(),
            TotalBatches = totalBatches,
            MaxChangedAt = maxChangedAt,
            WindowTo = DateTimeOffset.UtcNow,
            IsBackfill = isBackfill,
            IsBackfillCompleted = false,
            PagesFetched = 1,
        };
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ProcessAsync_FetchUnfinishedAtAnyRecordedCount_DoesNotFinalise(int recorded)
    {
        JobRun run = BuildJobRun();
        SetupDefaults(run, totalBatches: 1, recordedBatches: recorded);
        run.ExpectedBatchCount = null;

        // A backfill that has published one day out of many: the checkpoint carries the running
        // batch count with IsFetchCompleted left false, so no recorded count can settle the run.
        _jobCheckpointRepo
            .Setup(r => r.GetByJobRunIdAsync(run.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobCheckpoint
            {
                JobRunId = run.Id,
                Payload = JsonSerializer.Serialize(new OrdersImportCheckpointPayload
                {
                    Phase = OrdersImportCheckpointPhase.InProgress,
                    TotalBatches = 1,
                    MaxChangedAt = FetchedMaxChangedAt,
                    WindowTo = DateTimeOffset.UtcNow,
                    PagesFetched = 1,
                    IsBackfill = false,
                }),
                UpdatedAt = DateTimeOffset.UtcNow,
            });

        OrdersBatchImportCompletedEvent message = BuildEvent(run.Id);

        await CreateConsumer().ProcessAsync(message, message.BatchId, CancellationToken.None);

        run.Status.Should().Be(JobStatus.Running);
        run.CompletedAt.Should().BeNull();
        _jobRunRepo.Verify(r => r.UpdateAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()), Times.Never);
        _syncCheckpointRepo.Verify(r => r.UpsertAsync(It.IsAny<SyncCheckpoint>(), It.IsAny<CancellationToken>()), Times.Never);
        _eventPublisher.Verify(p => p.PublishAsync(It.IsAny<OrdersImportCompletedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_FetchFinishedButCheckpointCountHigher_DoesNotFinaliseEarly()
    {
        JobRun run = BuildJobRun();
        SetupDefaults(run, totalBatches: 3, recordedBatches: 1);
        run.ExpectedBatchCount = null;
        _jobRunRepo.Setup(r => r.GetBatchResultTotalsAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(BuildTotals(2, 0, 0, 0));
        OrdersBatchImportCompletedEvent message = BuildEvent(run.Id);

        await CreateConsumer().ProcessAsync(message, message.BatchId, CancellationToken.None);

        // The checkpoint is authoritative once the fetch finished: three batches were announced and
        // only one result is stored, so the remaining two are still in flight.
        run.Status.Should().Be(JobStatus.Running);
        run.CompletedAt.Should().BeNull();
        _jobRunRepo.Verify(r => r.UpdateAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()), Times.Never);
        _syncCheckpointRepo.Verify(r => r.UpsertAsync(It.IsAny<SyncCheckpoint>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_CompletionEventArrivesAfterFinalisationFromCheckpoint_DoesNotFinaliseTwice()
    {
        JobRun run = BuildJobRun();
        SetupDefaults(run, totalBatches: 1, recordedBatches: 1);
        SetupConnection(run);
        run.ExpectedBatchCount = null;
        _jobRunRepo.Setup(r => r.GetBatchResultTotalsAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(BuildTotals(3, 1, 1, 0));
        OrdersBatchImportCompletedEvent batchMessage = BuildEvent(run.Id);
        ProcessOrdersBatchImportCompletedConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(batchMessage, batchMessage.BatchId, CancellationToken.None);
        run.Status.Should().Be(JobStatus.Success, "the checkpoint announced the only batch of this run");

        await consumer.ProcessFetchCompletedAsync(BuildFetchCompletedEvent(run.Id, totalBatches: 1), CancellationToken.None);

        run.Status.Should().Be(JobStatus.Success);
        _jobRunRepo.Verify(r => r.UpdateAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()), Times.Once);
        _syncCheckpointRepo.Verify(r => r.UpsertAsync(It.IsAny<SyncCheckpoint>(), It.IsAny<CancellationToken>()), Times.Once);
        _eventPublisher.Verify(
            p => p.PublishAsync(It.IsAny<OrdersImportCompletedEvent>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2),
            "the late completion event finalises nothing but still nudges downstream consumers");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ProcessAsync_TerminalResultInAnyPosition_FinalisesOnlyAfterAllResults(int terminalPosition)
    {
        JobRun run = BuildJobRun();
        SetupDefaults(run, totalBatches: 3);
        int recorded = 0;
        _jobRunRepo.Setup(r => r.CountBatchResultsAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(() => recorded);
        _jobRunRepo.Setup(r => r.GetBatchResultTotalsAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(BuildTotals(2, 0, 0, 0));
        ProcessOrdersBatchImportCompletedConsumer consumer = CreateConsumer();

        for (int index = 0; index < 3; index++)
        {
            OrdersBatchImportCompletedEvent message = BuildEvent(run.Id, created: index == terminalPosition ? 0 : 1, updated: 0, skipped: 0);
            recorded++;
            await consumer.ProcessAsync(message, message.BatchId, CancellationToken.None);
            if (index < 2)
            {
                run.Status.Should().Be(JobStatus.Running);
                _jobRunRepo.Verify(r => r.UpdateAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()), Times.Never);
            }
        }

        run.Status.Should().Be(JobStatus.Success);
        run.RecordsCreated.Should().Be(2);
        _jobRunRepo.Verify(r => r.UpdateAsync(run, It.IsAny<CancellationToken>()), Times.Once);
        _syncCheckpointRepo.Verify(r => r.UpsertAsync(It.IsAny<SyncCheckpoint>(), It.IsAny<CancellationToken>()), Times.Once);
        _eventPublisher.Verify(p => p.PublishAsync(It.IsAny<OrdersImportCompletedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_LegacyJsonPayloadWithoutBatchCount_StillFinalisesOnJobRunThreshold()
    {
        JobRun run = BuildJobRun();
        SetupDefaults(run);
        _jobRunRepo.Setup(r => r.GetBatchResultTotalsAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(BuildTotals(3, 1, 1, 0));
        _jobCheckpointRepo.Setup(r => r.GetByJobRunIdAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new JobCheckpoint
        {
            JobRunId = run.Id,
            Payload = "{\"TotalBatches\":1}",
        });
        OrdersBatchImportCompletedEvent message = BuildEvent(run.Id);

        await CreateConsumer().ProcessAsync(message, message.BatchId, CancellationToken.None);

        run.Status.Should().Be(JobStatus.Success);
        _jobRunRepo.Verify(r => r.UpdateAsync(run, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessFetchCompletedAsync_BeforeAllBatchResults_RecordsThresholdAndWaits()
    {
        JobRun run = BuildJobRun();
        SetupDefaults(run, totalBatches: 3, recordedBatches: 1);
        run.ExpectedBatchCount = null;

        await CreateConsumer().ProcessFetchCompletedAsync(
            BuildFetchCompletedEvent(run.Id, totalBatches: 3),
            CancellationToken.None);

        run.ExpectedBatchCount.Should().Be(3);
        run.Status.Should().Be(JobStatus.Running);
        run.CompletedAt.Should().BeNull();
        _jobRunRepo.Verify(r => r.UpdateAsync(run, It.IsAny<CancellationToken>()), Times.Once);
        _eventPublisher.Verify(
            p => p.PublishAsync(It.IsAny<OrdersImportCompletedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessFetchCompletedAsync_AfterAllBatchResults_FinalisesImmediately()
    {
        JobRun run = BuildJobRun();
        SetupDefaults(run, totalBatches: 2, recordedBatches: 2);
        SetupConnection(run);
        run.ExpectedBatchCount = null;
        _jobRunRepo.Setup(r => r.GetBatchResultTotalsAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(BuildTotals(4, 1, 0, 0));

        await CreateConsumer().ProcessFetchCompletedAsync(
            BuildFetchCompletedEvent(run.Id, totalBatches: 2),
            CancellationToken.None);

        run.Status.Should().Be(JobStatus.Success);
        run.RecordsCreated.Should().Be(4);
        run.CompletedAt.Should().NotBeNull();
        _syncCheckpointRepo.Verify(r => r.UpsertAsync(It.IsAny<SyncCheckpoint>(), It.IsAny<CancellationToken>()), Times.Once);
        _eventPublisher.Verify(
            p => p.PublishAsync(It.IsAny<OrdersImportCompletedEvent>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessFetchCompletedAsync_ArrivingTwice_DoesNotDoubleCount()
    {
        JobRun run = BuildJobRun();
        SetupDefaults(run, totalBatches: 1, recordedBatches: 1);
        SetupConnection(run);
        run.ExpectedBatchCount = null;
        _jobRunRepo.Setup(r => r.GetBatchResultTotalsAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(BuildTotals(2, 0, 0, 0));
        OrdersImportFetchCompletedEvent message = BuildFetchCompletedEvent(run.Id, totalBatches: 1);
        ProcessOrdersBatchImportCompletedConsumer consumer = CreateConsumer();

        await consumer.ProcessFetchCompletedAsync(message, CancellationToken.None);
        await consumer.ProcessFetchCompletedAsync(message, CancellationToken.None);

        run.RecordsCreated.Should().Be(2);
        _jobRunRepo.Verify(r => r.UpdateAsync(run, It.IsAny<CancellationToken>()), Times.Exactly(2));
        _syncCheckpointRepo.Verify(r => r.UpsertAsync(It.IsAny<SyncCheckpoint>(), It.IsAny<CancellationToken>()), Times.Once);
        _eventPublisher.Verify(
            p => p.PublishAsync(It.IsAny<OrdersImportCompletedEvent>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2),
            "the redelivered completion event must still nudge downstream consumers");
    }

    [Fact]
    public async Task ProcessFetchCompletedAsync_ThresholdAlreadySet_DoesNotRewriteJobRun()
    {
        JobRun run = BuildJobRun();
        SetupDefaults(run, totalBatches: 3, recordedBatches: 1);

        await CreateConsumer().ProcessFetchCompletedAsync(
            BuildFetchCompletedEvent(run.Id, totalBatches: 3),
            CancellationToken.None);

        run.ExpectedBatchCount.Should().Be(3);
        run.Status.Should().Be(JobStatus.Running);
        _jobRunRepo.Verify(r => r.UpdateAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessFetchCompletedAsync_ConcurrentBatchResultFinalisedFirst_SteersAside()
    {
        JobRun run = BuildJobRun();
        SetupDefaults(run, totalBatches: 2, recordedBatches: 2);
        SetupConnection(run);
        run.ExpectedBatchCount = null;
        _jobRunRepo.Setup(r => r.GetBatchResultTotalsAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(BuildTotals(5, 0, 0, 0));
        _jobRunRepo
            .Setup(r => r.UpdateAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateConcurrencyException());

        await CreateConsumer().ProcessFetchCompletedAsync(
            BuildFetchCompletedEvent(run.Id, totalBatches: 2),
            CancellationToken.None);

        _syncCheckpointRepo.Verify(
            r => r.UpsertAsync(It.IsAny<SyncCheckpoint>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "the batch result that won the row version already wrote the watermark");
        _eventPublisher.Verify(
            p => p.PublishAsync(It.IsAny<OrdersImportCompletedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "the winning finalisation published the completed event");
    }

    [Fact]
    public async Task HealthyOrdering_BatchBeforeFetchCompleted_ProducesNoWarning()
    {
        // The production ordering, reproduced step by step: a delta run publishes its only batch
        // mid-fetch, so the batch result is consumed while the checkpoint still says the fetch is
        // unfinished and before the completion event has written the threshold. Neither delivery
        // may log a warning — this is the shape of a healthy run, and treating it as an anomaly
        // left the warning firing twice an hour forever.
        JobRun run = BuildJobRun();
        SetupDefaults(run, totalBatches: 1, recordedBatches: 1);
        SetupConnection(run);
        run.ExpectedBatchCount = null;
        _jobRunRepo.Setup(r => r.GetBatchResultTotalsAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(BuildTotals(3, 1, 1, 0));

        // OrdersBatchPublisher.PublishUnitAsync writes this checkpoint after publishing the batch,
        // and FinishCompletedAsync overwrites it once the fetch is over.
        OrdersImportCheckpointPayload fetched = new OrdersImportCheckpointPayload
        {
            Phase = OrdersImportCheckpointPhase.InProgress,
            TotalBatches = 1,
            MaxChangedAt = FetchedMaxChangedAt,
            WindowTo = DateTimeOffset.UtcNow,
            PagesFetched = 1,
            IsBackfill = false,
        };

        _jobCheckpointRepo
            .Setup(r => r.GetByJobRunIdAsync(run.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new JobCheckpoint
            {
                JobRunId = run.Id,
                Payload = JsonSerializer.Serialize(fetched),
                UpdatedAt = DateTimeOffset.UtcNow,
            });

        ProcessOrdersBatchImportCompletedConsumer consumer = CreateConsumer();
        OrdersBatchImportCompletedEvent batchMessage = BuildEvent(run.Id);

        await consumer.ProcessAsync(batchMessage, batchMessage.BatchId, CancellationToken.None);

        run.Status.Should().Be(JobStatus.Running, "the fetch outcome is not known when the batch result arrives");

        fetched = fetched with { Phase = OrdersImportCheckpointPhase.FetchCompleted };

        await consumer.ProcessFetchCompletedAsync(BuildFetchCompletedEvent(run.Id, totalBatches: 1), CancellationToken.None);

        run.Status.Should().Be(JobStatus.Success);
        run.RecordsCreated.Should().Be(3);
        WarningMessages().Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessFetchCompletedAsync_ZeroBatches_FinalisesAsSuccessWithoutBatches()
    {
        JobRun run = BuildJobRun();
        SetupDefaults(run, totalBatches: 0, recordedBatches: 0);
        SetupConnection(run);
        run.ExpectedBatchCount = null;
        _jobRunRepo.Setup(r => r.GetBatchResultTotalsAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(BuildTotals(0, 0, 0, 0));

        await CreateConsumer().ProcessFetchCompletedAsync(
            BuildFetchCompletedEvent(run.Id, totalBatches: 0),
            CancellationToken.None);

        run.Status.Should().Be(JobStatus.Success);
        run.RecordsCreated.Should().Be(0);
        _eventPublisher.Verify(
            p => p.PublishAsync(It.IsAny<OrdersImportCompletedEvent>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
