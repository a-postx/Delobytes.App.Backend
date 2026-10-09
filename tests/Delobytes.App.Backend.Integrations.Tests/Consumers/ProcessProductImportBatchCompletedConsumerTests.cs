using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.Consumers;

public class ProcessProductImportBatchCompletedConsumerTests
{
    private readonly Mock<ISyncJobRepository> _syncJobRepo = new();
    private readonly Mock<IEventPublisher> _eventPublisher = new();
    private readonly Mock<ILogger<ProcessProductImportBatchCompletedConsumer>> _logger = new();

    private ProcessProductImportBatchCompletedConsumer CreateConsumer()
    {
        return new ProcessProductImportBatchCompletedConsumer(
            _syncJobRepo.Object,
            _eventPublisher.Object,
            _logger.Object);
    }

    private static SyncJob BuildSyncJob(SyncJobStatus status = SyncJobStatus.Running)
    {
        return new SyncJob
        {
            Id = Guid.NewGuid(),
            ConnectionId = Guid.NewGuid(),
            JobType = JobType.ProductsImport,
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    private static ProductImportBatchCompletedEvent BuildEvent(
        Guid syncJobId,
        bool isLastBatch = false,
        int processed = 5,
        int created = 3,
        int updated = 1,
        int skipped = 1,
        int failed = 0,
        string? errorMessage = null,
        int totalBatches = 1)
    {
        return new ProductImportBatchCompletedEvent
        {
            SyncJobId = syncJobId,
            IsLastBatch = isLastBatch,
            RecordsProcessed = processed,
            RecordsCreated = created,
            RecordsUpdated = updated,
            RecordsSkipped = skipped,
            RecordsFailed = failed,
            ErrorMessage = errorMessage,
            TotalBatches = totalBatches,
        };
    }

    // -----------------------------------------------------------------------
    // SyncJob not found
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_SyncJobNotFound_SkipsWithoutAction()
    {
        Guid syncJobId = Guid.NewGuid();

        _syncJobRepo
            .Setup(r => r.FindByIdAsync(syncJobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncJob?)null);

        ProcessProductImportBatchCompletedConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(BuildEvent(syncJobId), Guid.NewGuid(), CancellationToken.None);

        _syncJobRepo.Verify(r => r.AddBatchResult(It.IsAny<SyncJobBatchResult>()), Times.Never);
        _eventPublisher.Verify(
            p => p.PublishAsync(It.IsAny<ProductsImportCompletedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // -----------------------------------------------------------------------
    // Cancelled SyncJob — drop silently
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_CancelledJob_DropsSilently()
    {
        SyncJob syncJob = BuildSyncJob(SyncJobStatus.Cancelled);

        _syncJobRepo
            .Setup(r => r.FindByIdAsync(syncJob.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(syncJob);

        ProcessProductImportBatchCompletedConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(BuildEvent(syncJob.Id), Guid.NewGuid(), CancellationToken.None);

        _syncJobRepo.Verify(r => r.AddBatchResult(It.IsAny<SyncJobBatchResult>()), Times.Never);
    }

    // -----------------------------------------------------------------------
    // Duplicate delivery — already processed MessageId
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_DuplicateMessageId_SkipsWithoutAddingResult()
    {
        SyncJob syncJob = BuildSyncJob();
        Guid messageId = Guid.NewGuid();

        _syncJobRepo
            .Setup(r => r.FindByIdAsync(syncJob.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(syncJob);

        _syncJobRepo
            .Setup(r => r.BatchResultExistsAsync(messageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        ProcessProductImportBatchCompletedConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(BuildEvent(syncJob.Id, isLastBatch: false), messageId, CancellationToken.None);

        _syncJobRepo.Verify(r => r.AddBatchResult(It.IsAny<SyncJobBatchResult>()), Times.Never);
        _syncJobRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // -----------------------------------------------------------------------
    // Duplicate last-batch redelivery after terminal status — re-publishes completed event
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_DuplicateLastBatch_TerminalJob_RepublishesCompletedEvent()
    {
        SyncJob syncJob = BuildSyncJob(SyncJobStatus.Success);
        Guid messageId = Guid.NewGuid();

        _syncJobRepo
            .Setup(r => r.FindByIdAsync(syncJob.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(syncJob);

        _syncJobRepo
            .Setup(r => r.BatchResultExistsAsync(messageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _eventPublisher
            .Setup(p => p.PublishAsync(It.IsAny<ProductsImportCompletedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        ProcessProductImportBatchCompletedConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(BuildEvent(syncJob.Id, isLastBatch: true), messageId, CancellationToken.None);

        _eventPublisher.Verify(
            p => p.PublishAsync(It.IsAny<ProductsImportCompletedEvent>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _syncJobRepo.Verify(r => r.AddBatchResult(It.IsAny<SyncJobBatchResult>()), Times.Never);
    }

    // -----------------------------------------------------------------------
    // Non-last batch — stored but no finalisation
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_NonLastBatch_StoredWithoutFinalisation()
    {
        SyncJob syncJob = BuildSyncJob();
        Guid messageId = Guid.NewGuid();

        _syncJobRepo
            .Setup(r => r.FindByIdAsync(syncJob.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(syncJob);

        _syncJobRepo
            .Setup(r => r.BatchResultExistsAsync(messageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _syncJobRepo
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        ProcessProductImportBatchCompletedConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(BuildEvent(syncJob.Id, isLastBatch: false), messageId, CancellationToken.None);

        _syncJobRepo.Verify(r => r.AddBatchResult(It.IsAny<SyncJobBatchResult>()), Times.Once);
        _syncJobRepo.Verify(r => r.Update(It.IsAny<SyncJob>()), Times.Never);
        _eventPublisher.Verify(
            p => p.PublishAsync(It.IsAny<ProductsImportCompletedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // -----------------------------------------------------------------------
    // Last batch, no errors → Success
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_LastBatch_NoErrors_FinalisesWithSuccess()
    {
        SyncJob syncJob = BuildSyncJob();
        Guid messageId = Guid.NewGuid();

        _syncJobRepo
            .Setup(r => r.FindByIdAsync(syncJob.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(syncJob);

        _syncJobRepo
            .Setup(r => r.BatchResultExistsAsync(messageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _syncJobRepo
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        SyncJobBatchResultTotals totals = new SyncJobBatchResultTotals
        {
            TotalProcessed = 10,
            TotalCreated = 7,
            TotalUpdated = 3,
            TotalSkipped = 0,
            TotalFailed = 0,
            CombinedErrors = null,
        };

        _syncJobRepo
            .Setup(r => r.GetBatchResultTotalsAsync(syncJob.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(totals);

        _syncJobRepo
            .Setup(r => r.CountBatchResultsAsync(syncJob.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _eventPublisher
            .Setup(p => p.PublishAsync(It.IsAny<ProductsImportCompletedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        ProcessProductImportBatchCompletedConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(BuildEvent(syncJob.Id, isLastBatch: true), messageId, CancellationToken.None);

        syncJob.Status.Should().Be(SyncJobStatus.Success);
        syncJob.RecordsProcessed.Should().Be(10);
        syncJob.RecordsCreated.Should().Be(7);
        syncJob.RecordsUpdated.Should().Be(3);
        syncJob.RecordsImported.Should().Be(10); // created + updated
        syncJob.ErrorMessage.Should().BeNull();
        syncJob.CompletedAt.Should().NotBeNull();

        // Two updates by design for the terminal batch: the first persists TotalImportBatches
        // while the job is still Running (so later-arriving batches can finalise), the second
        // writes the aggregated counters and the terminal status.
        _syncJobRepo.Verify(r => r.Update(syncJob), Times.Exactly(2));
        _eventPublisher.Verify(
            p => p.PublishAsync(It.IsAny<ProductsImportCompletedEvent>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // -----------------------------------------------------------------------
    // Last batch, partial errors → PartiallySucceeded
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_LastBatch_PartialErrors_FinalisesWithPartiallySucceeded()
    {
        SyncJob syncJob = BuildSyncJob();
        Guid messageId = Guid.NewGuid();

        _syncJobRepo
            .Setup(r => r.FindByIdAsync(syncJob.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(syncJob);

        _syncJobRepo
            .Setup(r => r.BatchResultExistsAsync(messageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _syncJobRepo
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        SyncJobBatchResultTotals totals = new SyncJobBatchResultTotals
        {
            TotalProcessed = 10,
            TotalCreated = 6,
            TotalUpdated = 2,
            TotalSkipped = 0,
            TotalFailed = 2,
            CombinedErrors = "NmId=1: bad data; NmId=2: timeout",
        };

        _syncJobRepo
            .Setup(r => r.GetBatchResultTotalsAsync(syncJob.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(totals);

        _syncJobRepo
            .Setup(r => r.CountBatchResultsAsync(syncJob.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _eventPublisher
            .Setup(p => p.PublishAsync(It.IsAny<ProductsImportCompletedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        ProcessProductImportBatchCompletedConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(BuildEvent(syncJob.Id, isLastBatch: true), messageId, CancellationToken.None);

        syncJob.Status.Should().Be(SyncJobStatus.PartiallySucceeded);
        syncJob.RecordsFailed.Should().Be(2);
        syncJob.ErrorMessage.Should().Be("NmId=1: bad data; NmId=2: timeout");

        _eventPublisher.Verify(
            p => p.PublishAsync(
                It.Is<ProductsImportCompletedEvent>(e => e.IsSuccess),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // -----------------------------------------------------------------------
    // Last batch, all failed → Failed
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_LastBatch_AllFailed_FinalisesWithFailed()
    {
        SyncJob syncJob = BuildSyncJob();
        Guid messageId = Guid.NewGuid();

        _syncJobRepo
            .Setup(r => r.FindByIdAsync(syncJob.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(syncJob);

        _syncJobRepo
            .Setup(r => r.BatchResultExistsAsync(messageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _syncJobRepo
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        SyncJobBatchResultTotals totals = new SyncJobBatchResultTotals
        {
            TotalProcessed = 5,
            TotalCreated = 0,
            TotalUpdated = 0,
            TotalSkipped = 0,
            TotalFailed = 5,
            CombinedErrors = "Channel not found",
        };

        _syncJobRepo
            .Setup(r => r.GetBatchResultTotalsAsync(syncJob.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(totals);

        _syncJobRepo
            .Setup(r => r.CountBatchResultsAsync(syncJob.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _eventPublisher
            .Setup(p => p.PublishAsync(It.IsAny<ProductsImportCompletedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        ProcessProductImportBatchCompletedConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(BuildEvent(syncJob.Id, isLastBatch: true), messageId, CancellationToken.None);

        syncJob.Status.Should().Be(SyncJobStatus.Failed);
        syncJob.ErrorMessage.Should().Be("Channel not found");

        _eventPublisher.Verify(
            p => p.PublishAsync(
                It.Is<ProductsImportCompletedEvent>(e => !e.IsSuccess),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // -----------------------------------------------------------------------
    // Last batch, only skipped records (no created/updated/failed) → Success
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_LastBatch_AllSkipped_FinalisesWithSuccess()
    {
        SyncJob syncJob = BuildSyncJob();
        Guid messageId = Guid.NewGuid();

        _syncJobRepo
            .Setup(r => r.FindByIdAsync(syncJob.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(syncJob);

        _syncJobRepo
            .Setup(r => r.BatchResultExistsAsync(messageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _syncJobRepo
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        SyncJobBatchResultTotals totals = new SyncJobBatchResultTotals
        {
            TotalProcessed = 5,
            TotalCreated = 0,
            TotalUpdated = 0,
            TotalSkipped = 5,
            TotalFailed = 0,
            CombinedErrors = null,
        };

        _syncJobRepo
            .Setup(r => r.GetBatchResultTotalsAsync(syncJob.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(totals);

        _syncJobRepo
            .Setup(r => r.CountBatchResultsAsync(syncJob.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _eventPublisher
            .Setup(p => p.PublishAsync(It.IsAny<ProductsImportCompletedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        ProcessProductImportBatchCompletedConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(BuildEvent(syncJob.Id, isLastBatch: true), messageId, CancellationToken.None);

        syncJob.Status.Should().Be(SyncJobStatus.Success);
    }

    // -----------------------------------------------------------------------
    // Batch result stores correct MessageId
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_NonLastBatch_StoresBatchResultWithCorrectMessageId()
    {
        SyncJob syncJob = BuildSyncJob();
        Guid messageId = Guid.NewGuid();
        SyncJobBatchResult? captured = null;

        _syncJobRepo
            .Setup(r => r.FindByIdAsync(syncJob.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(syncJob);

        _syncJobRepo
            .Setup(r => r.BatchResultExistsAsync(messageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _syncJobRepo
            .Setup(r => r.AddBatchResult(It.IsAny<SyncJobBatchResult>()))
            .Callback<SyncJobBatchResult>(r => captured = r);

        _syncJobRepo
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        ProcessProductImportBatchCompletedConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(
            BuildEvent(syncJob.Id, isLastBatch: false, processed: 3, created: 2, updated: 1),
            messageId,
            CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.MessageId.Should().Be(messageId);
        captured.SyncJobId.Should().Be(syncJob.Id);
        captured.RecordsProcessed.Should().Be(3);
        captured.RecordsCreated.Should().Be(2);
        captured.RecordsUpdated.Should().Be(1);
        captured.IsLastBatch.Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Regression test for the Wildberries import counters bug: 117 cards split into
    // batches of 50 + 50 + 17 (default BatchSize=50). The 17-card batch is the terminal
    // one (IsLastBatch=true), but — because it is the smallest — its completion event can
    // reach this aggregator before the two 50-card batches have finished processing.
    // The job must NOT be finalised with only 17 records counted; it must wait until all
    // three batch results are recorded, regardless of arrival order.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_TerminalBatchArrivesBeforeLargerBatches_WaitsForAllBatchesBeforeFinalising()
    {
        SyncJob syncJob = BuildSyncJob();

        List<SyncJobBatchResult> storedResults = new List<SyncJobBatchResult>();

        _syncJobRepo
            .Setup(r => r.FindByIdAsync(syncJob.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(syncJob);

        _syncJobRepo
            .Setup(r => r.BatchResultExistsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns((Guid messageId, CancellationToken _) =>
                Task.FromResult(storedResults.Any(r => r.MessageId == messageId)));

        _syncJobRepo
            .Setup(r => r.AddBatchResult(It.IsAny<SyncJobBatchResult>()))
            .Callback<SyncJobBatchResult>(r => storedResults.Add(r));

        _syncJobRepo
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _syncJobRepo
            .Setup(r => r.CountBatchResultsAsync(syncJob.Id, It.IsAny<CancellationToken>()))
            .Returns((Guid _, CancellationToken __) => Task.FromResult(storedResults.Count));

        _syncJobRepo
            .Setup(r => r.GetBatchResultTotalsAsync(syncJob.Id, It.IsAny<CancellationToken>()))
            .Returns((Guid _, CancellationToken __) => Task.FromResult(new SyncJobBatchResultTotals
            {
                TotalProcessed = storedResults.Sum(r => r.RecordsProcessed),
                TotalCreated = storedResults.Sum(r => r.RecordsCreated),
                TotalUpdated = storedResults.Sum(r => r.RecordsUpdated),
                TotalSkipped = storedResults.Sum(r => r.RecordsSkipped),
                TotalFailed = storedResults.Sum(r => r.RecordsFailed),
                CombinedErrors = null,
            }));

        _eventPublisher
            .Setup(p => p.PublishAsync(It.IsAny<ProductsImportCompletedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        ProcessProductImportBatchCompletedConsumer consumer = CreateConsumer();

        // 1) The terminal (smallest) batch finishes first: 17 cards, IsLastBatch=true,
        //    TotalBatches=3 — this is the only message that carries the total.
        await consumer.ProcessAsync(
            BuildEvent(syncJob.Id, isLastBatch: true, processed: 17, created: 17, updated: 0, skipped: 0, totalBatches: 3),
            Guid.NewGuid(),
            CancellationToken.None);

        syncJob.Status.Should().Be(SyncJobStatus.Running, "только 1 из 3 батчей учтён — финализация преждевременна");
        _eventPublisher.Verify(
            p => p.PublishAsync(It.IsAny<ProductsImportCompletedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);

        // 2) First 50-card batch arrives.
        await consumer.ProcessAsync(
            BuildEvent(syncJob.Id, isLastBatch: false, processed: 50, created: 50, updated: 0, skipped: 0),
            Guid.NewGuid(),
            CancellationToken.None);

        syncJob.Status.Should().Be(SyncJobStatus.Running, "учтено 2 из 3 батчей");
        _eventPublisher.Verify(
            p => p.PublishAsync(It.IsAny<ProductsImportCompletedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);

        // 3) Second (last-arriving) 50-card batch — NOT flagged IsLastBatch, yet it is the
        //    one that completes the count and must trigger finalisation.
        await consumer.ProcessAsync(
            BuildEvent(syncJob.Id, isLastBatch: false, processed: 50, created: 50, updated: 0, skipped: 0),
            Guid.NewGuid(),
            CancellationToken.None);

        syncJob.Status.Should().Be(SyncJobStatus.Success);
        syncJob.RecordsProcessed.Should().Be(117);
        syncJob.RecordsCreated.Should().Be(117);
        syncJob.RecordsImported.Should().Be(117);

        _eventPublisher.Verify(
            p => p.PublishAsync(
                It.Is<ProductsImportCompletedEvent>(e =>
                    e.TotalRecordsProcessed == 117 && e.TotalRecordsCreated == 117),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // -----------------------------------------------------------------------
    // Concurrency guard: two batch-completion events both reach the finalisation branch
    // for the same SyncJob (e.g. two consumer instances racing after both observing
    // recordedBatches == TotalImportBatches). Status is a concurrency token
    // (SyncJobConfiguration.IsConcurrencyToken), so the loser's SaveChanges throws
    // DbUpdateConcurrencyException instead of double-finalising / double-publishing.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_ConcurrentFinalisation_ConcurrencyConflictIsSwallowedWithoutRepublishing()
    {
        SyncJob syncJob = BuildSyncJob();
        Guid messageId = Guid.NewGuid();

        _syncJobRepo
            .Setup(r => r.FindByIdAsync(syncJob.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(syncJob);

        _syncJobRepo
            .Setup(r => r.BatchResultExistsAsync(messageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _syncJobRepo
            .Setup(r => r.GetBatchResultTotalsAsync(syncJob.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncJobBatchResultTotals
            {
                TotalProcessed = 1,
                TotalCreated = 1,
                TotalUpdated = 0,
                TotalSkipped = 0,
                TotalFailed = 0,
                CombinedErrors = null,
            });

        _syncJobRepo
            .Setup(r => r.CountBatchResultsAsync(syncJob.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // First SaveChanges (recording the batch result + TotalImportBatches) succeeds;
        // the second one (the finalising status update) simulates losing the race.
        int saveCallCount = 0;
        _syncJobRepo
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                saveCallCount++;
                return saveCallCount == 2
                    ? throw new DbUpdateConcurrencyException()
                    : Task.FromResult(1);
            });

        ProcessProductImportBatchCompletedConsumer consumer = CreateConsumer();

        Func<Task> act = () => consumer.ProcessAsync(
            BuildEvent(syncJob.Id, isLastBatch: true, processed: 1, created: 1, updated: 0, skipped: 0, totalBatches: 1),
            messageId,
            CancellationToken.None);

        await act.Should().NotThrowAsync("конфликт конкуренции должен поглощаться, а не падать наружу");

        _eventPublisher.Verify(
            p => p.PublishAsync(It.IsAny<ProductsImportCompletedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "победитель гонки публикует событие сам — проигравший не должен его дублировать");
    }
}
