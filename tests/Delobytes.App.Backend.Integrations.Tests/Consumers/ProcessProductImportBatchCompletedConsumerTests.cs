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
    private readonly Mock<IJobRunRepository> _jobRunRepo = new();
    private readonly Mock<ISyncCheckpointRepository> _syncCheckpointRepo = new();
    private readonly Mock<IConnectionRepository> _connectionRepo = new();
    private readonly Mock<IEventPublisher> _eventPublisher = new();
    private readonly Mock<ILogger<ProcessProductImportBatchCompletedConsumer>> _logger = new();

    private ProcessProductImportBatchCompletedConsumer CreateConsumer()
    {
        return new ProcessProductImportBatchCompletedConsumer(
            _jobRunRepo.Object,
            _syncCheckpointRepo.Object,
            _connectionRepo.Object,
            _eventPublisher.Object,
            _logger.Object);
    }

    private static JobRun BuildJobRun(JobStatus status = JobStatus.Running)
    {
        return new JobRun
        {
            Id = Guid.NewGuid(),
            ConnectionId = Guid.NewGuid(),
            JobType = JobType.ProductsImport,
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    private static ProductsImportJob BuildImportJob(JobRun jobRun, int? totalImportBatches = null)
    {
        return new ProductsImportJob
        {
            JobRunId = jobRun.Id,
            TotalImportBatches = totalImportBatches,
        };
    }

    private static ProductBatchImportCompletedEvent BuildEvent(
        Guid jobRunId,
        bool isLastBatch = false,
        int processed = 5,
        int created = 3,
        int updated = 1,
        int skipped = 1,
        int failed = 0,
        string? errorMessage = null,
        int totalBatches = 1)
    {
        return new ProductBatchImportCompletedEvent
        {
            JobRunId = jobRunId,
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

    /// <summary>
    /// Wires up the ProductsImportJob lookup and the SaveChangesAsync/UpdateAsync happy path
    /// most tests rely on. Individual tests override specific setups (totals, counts) afterwards.
    /// </summary>
    private void SetupDefaults(JobRun jobRun, ProductsImportJob importJob)
    {
        _jobRunRepo
            .Setup(r => r.GetByIdAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(jobRun);

        _jobRunRepo
            .Setup(r => r.GetProductsImportJobAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(importJob);

        _jobRunRepo
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _jobRunRepo
            .Setup(r => r.UpdateAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _eventPublisher
            .Setup(p => p.PublishAsync(It.IsAny<ProductsImportCompletedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    // -----------------------------------------------------------------------
    // JobRun not found
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_JobRunNotFound_SkipsWithoutAction()
    {
        Guid jobRunId = Guid.NewGuid();

        _jobRunRepo
            .Setup(r => r.GetByIdAsync(jobRunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((JobRun?)null);

        ProcessProductImportBatchCompletedConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(BuildEvent(jobRunId), Guid.NewGuid(), CancellationToken.None);

        _jobRunRepo.Verify(r => r.AddBatchResult(It.IsAny<JobRunBatchResult>()), Times.Never);
        _eventPublisher.Verify(
            p => p.PublishAsync(It.IsAny<ProductsImportCompletedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // -----------------------------------------------------------------------
    // Cancelled JobRun -> batch result is still recorded and counters still accumulated
    // (unlike the old SyncJob model, which dropped incoming results for a Cancelled job)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_CancelledJob_StillRecordsBatchResultAndAccumulatesCounters()
    {
        JobRun jobRun = BuildJobRun(JobStatus.Cancelled);
        ProductsImportJob importJob = BuildImportJob(jobRun);
        Guid messageId = Guid.NewGuid();

        SetupDefaults(jobRun, importJob);

        _jobRunRepo
            .Setup(r => r.BatchResultExistsAsync(messageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        ProcessProductImportBatchCompletedConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(BuildEvent(jobRun.Id, isLastBatch: false), messageId, CancellationToken.None);

        _jobRunRepo.Verify(r => r.AddBatchResult(It.IsAny<JobRunBatchResult>()), Times.Once);
        _jobRunRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _eventPublisher.Verify(
            p => p.PublishAsync(It.IsAny<ProductsImportCompletedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "finalisation only happens once every expected batch has been recorded");
    }

    [Fact]
    public async Task ProcessAsync_CancelledJob_LastBatchFinalisesAsCancelledPreservingPartialCounters()
    {
        JobRun jobRun = BuildJobRun(JobStatus.Cancelled);
        ProductsImportJob importJob = BuildImportJob(jobRun);
        Guid messageId = Guid.NewGuid();

        SetupDefaults(jobRun, importJob);

        _jobRunRepo
            .Setup(r => r.BatchResultExistsAsync(messageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _jobRunRepo
            .Setup(r => r.CountBatchResultsAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        JobRunBatchResultTotals totals = new JobRunBatchResultTotals
        {
            TotalProcessed = 7,
            TotalCreated = 4,
            TotalUpdated = 3,
            TotalSkipped = 0,
            TotalFailed = 0,
            CombinedErrors = null,
        };

        _jobRunRepo
            .Setup(r => r.GetBatchResultTotalsAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(totals);

        ProcessProductImportBatchCompletedConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(
            BuildEvent(jobRun.Id, isLastBatch: true, processed: 7, created: 4, updated: 3, skipped: 0, totalBatches: 1),
            messageId,
            CancellationToken.None);

        // Status remains Cancelled (not overwritten to Success/Failed) while the counters
        // reflect what was actually processed before the cooperative stop took effect.
        jobRun.Status.Should().Be(JobStatus.Cancelled);
        jobRun.RecordsCreated.Should().Be(4);
        jobRun.RecordsUpdated.Should().Be(3);
        jobRun.CompletedAt.Should().NotBeNull();

        _eventPublisher.Verify(
            p => p.PublishAsync(
                It.Is<ProductsImportCompletedEvent>(e => e.JobRunId == jobRun.Id && !e.IsSuccess),
                It.IsAny<CancellationToken>()),
            Times.Once);

        // A Cancelled run never advances the watermark.
        _syncCheckpointRepo.Verify(
            r => r.UpsertAsync(It.IsAny<SyncCheckpoint>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // -----------------------------------------------------------------------
    // Duplicate delivery - already processed MessageId
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_DuplicateMessageId_SkipsWithoutAddingResult()
    {
        JobRun jobRun = BuildJobRun();
        Guid messageId = Guid.NewGuid();

        _jobRunRepo
            .Setup(r => r.GetByIdAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(jobRun);

        _jobRunRepo
            .Setup(r => r.BatchResultExistsAsync(messageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        ProcessProductImportBatchCompletedConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(BuildEvent(jobRun.Id, isLastBatch: false), messageId, CancellationToken.None);

        _jobRunRepo.Verify(r => r.AddBatchResult(It.IsAny<JobRunBatchResult>()), Times.Never);
        _jobRunRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // -----------------------------------------------------------------------
    // Duplicate last-batch redelivery after terminal status -> re-publishes completed event
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_DuplicateLastBatch_TerminalJob_RepublishesCompletedEvent()
    {
        JobRun jobRun = BuildJobRun(JobStatus.Success);
        Guid messageId = Guid.NewGuid();

        _jobRunRepo
            .Setup(r => r.GetByIdAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(jobRun);

        _jobRunRepo
            .Setup(r => r.BatchResultExistsAsync(messageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _eventPublisher
            .Setup(p => p.PublishAsync(It.IsAny<ProductsImportCompletedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        ProcessProductImportBatchCompletedConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(BuildEvent(jobRun.Id, isLastBatch: true), messageId, CancellationToken.None);

        _eventPublisher.Verify(
            p => p.PublishAsync(It.IsAny<ProductsImportCompletedEvent>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _jobRunRepo.Verify(r => r.AddBatchResult(It.IsAny<JobRunBatchResult>()), Times.Never);
    }

    // -----------------------------------------------------------------------
    // Non-last batch -> stored but no finalisation
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_NonLastBatch_StoredWithoutFinalisation()
    {
        JobRun jobRun = BuildJobRun();
        ProductsImportJob importJob = BuildImportJob(jobRun);
        Guid messageId = Guid.NewGuid();

        SetupDefaults(jobRun, importJob);

        _jobRunRepo
            .Setup(r => r.BatchResultExistsAsync(messageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        ProcessProductImportBatchCompletedConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(BuildEvent(jobRun.Id, isLastBatch: false), messageId, CancellationToken.None);

        _jobRunRepo.Verify(r => r.AddBatchResult(It.IsAny<JobRunBatchResult>()), Times.Once);
        _jobRunRepo.Verify(r => r.UpdateAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()), Times.Never);
        _eventPublisher.Verify(
            p => p.PublishAsync(It.IsAny<ProductsImportCompletedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);

        // TotalImportBatches is still unknown (no batch has been flagged IsLastBatch yet), so
        // the aggregator cannot possibly know it has recorded them all.
        importJob.TotalImportBatches.Should().BeNull();
    }

    // -----------------------------------------------------------------------
    // Last batch, no errors -> Success
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_LastBatch_NoErrors_FinalisesWithSuccess()
    {
        JobRun jobRun = BuildJobRun();
        ProductsImportJob importJob = BuildImportJob(jobRun);
        Guid messageId = Guid.NewGuid();

        SetupDefaults(jobRun, importJob);

        _jobRunRepo
            .Setup(r => r.BatchResultExistsAsync(messageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        JobRunBatchResultTotals totals = new JobRunBatchResultTotals
        {
            TotalProcessed = 10,
            TotalCreated = 7,
            TotalUpdated = 3,
            TotalSkipped = 0,
            TotalFailed = 0,
            CombinedErrors = null,
        };

        _jobRunRepo
            .Setup(r => r.GetBatchResultTotalsAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(totals);

        _jobRunRepo
            .Setup(r => r.CountBatchResultsAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        SyncCheckpoint syncCheckpoint = new SyncCheckpoint
        {
            Id = Guid.NewGuid(),
            ConnectionId = jobRun.ConnectionId,
            JobType = jobRun.JobType,
            LastChangedAt = null,
            LastSuccessAt = DateTimeOffset.UtcNow.AddDays(-30),
        };

        _syncCheckpointRepo
            .Setup(r => r.GetAsync(jobRun.ConnectionId, jobRun.JobType, It.IsAny<CancellationToken>()))
            .ReturnsAsync(syncCheckpoint);

        _syncCheckpointRepo
            .Setup(r => r.UpsertAsync(It.IsAny<SyncCheckpoint>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        Connection connection = new Connection
        {
            Id = jobRun.ConnectionId,
            ChannelId = Guid.NewGuid(),
            Name = "Test WB",
            ApiKey = "test-key",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _connectionRepo
            .Setup(r => r.FindByIdAsync(jobRun.ConnectionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);

        _connectionRepo
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        ProcessProductImportBatchCompletedConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(BuildEvent(jobRun.Id, isLastBatch: true), messageId, CancellationToken.None);

        jobRun.Status.Should().Be(JobStatus.Success);
        jobRun.RecordsCreated.Should().Be(7);
        jobRun.RecordsUpdated.Should().Be(3);
        jobRun.Error.Should().BeNull();
        jobRun.CompletedAt.Should().NotBeNull();

        // SaveChangesAsync persists the batch result together with ProductsImportJob.TotalImportBatches
        // while the job is still Running (so later-arriving batches can still finalise it); UpdateAsync
        // is the separate call that writes the aggregated counters and the terminal status.
        _jobRunRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _jobRunRepo.Verify(r => r.UpdateAsync(jobRun, It.IsAny<CancellationToken>()), Times.Once);

        _syncCheckpointRepo.Verify(
            r => r.UpsertAsync(
                It.Is<SyncCheckpoint>(c => c.LastJobRunId == jobRun.Id),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "a successful run must advance the watermark");

        connection.LastSyncAt.Should().NotBeNull();

        _eventPublisher.Verify(
            p => p.PublishAsync(It.IsAny<ProductsImportCompletedEvent>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // -----------------------------------------------------------------------
    // Last batch, some failures -> Failed (JobStatus has no "partially succeeded" value, unlike
    // the old SyncJobStatus: any failed record makes the run Failed)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_LastBatch_PartialErrors_FinalisesWithFailed()
    {
        JobRun jobRun = BuildJobRun();
        ProductsImportJob importJob = BuildImportJob(jobRun);
        Guid messageId = Guid.NewGuid();

        SetupDefaults(jobRun, importJob);

        _jobRunRepo
            .Setup(r => r.BatchResultExistsAsync(messageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        JobRunBatchResultTotals totals = new JobRunBatchResultTotals
        {
            TotalProcessed = 10,
            TotalCreated = 6,
            TotalUpdated = 2,
            TotalSkipped = 0,
            TotalFailed = 2,
            CombinedErrors = "NmId=1: bad data; NmId=2: timeout",
        };

        _jobRunRepo
            .Setup(r => r.GetBatchResultTotalsAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(totals);

        _jobRunRepo
            .Setup(r => r.CountBatchResultsAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _connectionRepo
            .Setup(r => r.FindByIdAsync(jobRun.ConnectionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Connection?)null);

        ProcessProductImportBatchCompletedConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(BuildEvent(jobRun.Id, isLastBatch: true), messageId, CancellationToken.None);

        jobRun.Status.Should().Be(JobStatus.Failed);
        jobRun.RecordsFailed.Should().Be(2);
        jobRun.Error.Should().Be("NmId=1: bad data; NmId=2: timeout");

        _eventPublisher.Verify(
            p => p.PublishAsync(
                It.Is<ProductsImportCompletedEvent>(e => !e.IsSuccess),
                It.IsAny<CancellationToken>()),
            Times.Once);

        // A Failed run never advances the watermark.
        _syncCheckpointRepo.Verify(
            r => r.UpsertAsync(It.IsAny<SyncCheckpoint>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // -----------------------------------------------------------------------
    // Last batch, all failed -> Failed
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_LastBatch_AllFailed_FinalisesWithFailed()
    {
        JobRun jobRun = BuildJobRun();
        ProductsImportJob importJob = BuildImportJob(jobRun);
        Guid messageId = Guid.NewGuid();

        SetupDefaults(jobRun, importJob);

        _jobRunRepo
            .Setup(r => r.BatchResultExistsAsync(messageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        JobRunBatchResultTotals totals = new JobRunBatchResultTotals
        {
            TotalProcessed = 5,
            TotalCreated = 0,
            TotalUpdated = 0,
            TotalSkipped = 0,
            TotalFailed = 5,
            CombinedErrors = "Channel not found",
        };

        _jobRunRepo
            .Setup(r => r.GetBatchResultTotalsAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(totals);

        _jobRunRepo
            .Setup(r => r.CountBatchResultsAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _connectionRepo
            .Setup(r => r.FindByIdAsync(jobRun.ConnectionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Connection?)null);

        ProcessProductImportBatchCompletedConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(BuildEvent(jobRun.Id, isLastBatch: true), messageId, CancellationToken.None);

        jobRun.Status.Should().Be(JobStatus.Failed);
        jobRun.Error.Should().Be("Channel not found");

        _eventPublisher.Verify(
            p => p.PublishAsync(
                It.Is<ProductsImportCompletedEvent>(e => !e.IsSuccess),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // -----------------------------------------------------------------------
    // Last batch, only skipped records (no created/updated/failed) -> Success
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_LastBatch_AllSkipped_FinalisesWithSuccess()
    {
        JobRun jobRun = BuildJobRun();
        ProductsImportJob importJob = BuildImportJob(jobRun);
        Guid messageId = Guid.NewGuid();

        SetupDefaults(jobRun, importJob);

        _jobRunRepo
            .Setup(r => r.BatchResultExistsAsync(messageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        JobRunBatchResultTotals totals = new JobRunBatchResultTotals
        {
            TotalProcessed = 5,
            TotalCreated = 0,
            TotalUpdated = 0,
            TotalSkipped = 5,
            TotalFailed = 0,
            CombinedErrors = null,
        };

        _jobRunRepo
            .Setup(r => r.GetBatchResultTotalsAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(totals);

        _jobRunRepo
            .Setup(r => r.CountBatchResultsAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _connectionRepo
            .Setup(r => r.FindByIdAsync(jobRun.ConnectionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Connection?)null);

        ProcessProductImportBatchCompletedConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(BuildEvent(jobRun.Id, isLastBatch: true), messageId, CancellationToken.None);

        jobRun.Status.Should().Be(JobStatus.Success);
    }

    // -----------------------------------------------------------------------
    // Batch result stores correct JobRunId/MessageId
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_NonLastBatch_StoresBatchResultWithCorrectJobRunIdAndMessageId()
    {
        JobRun jobRun = BuildJobRun();
        ProductsImportJob importJob = BuildImportJob(jobRun);
        Guid messageId = Guid.NewGuid();
        JobRunBatchResult? captured = null;

        SetupDefaults(jobRun, importJob);

        _jobRunRepo
            .Setup(r => r.BatchResultExistsAsync(messageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _jobRunRepo
            .Setup(r => r.AddBatchResult(It.IsAny<JobRunBatchResult>()))
            .Callback<JobRunBatchResult>(r => captured = r);

        ProcessProductImportBatchCompletedConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(
            BuildEvent(jobRun.Id, isLastBatch: false, processed: 9, created: 4, updated: 2, skipped: 3, failed: 0),
            messageId,
            CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.JobRunId.Should().Be(jobRun.Id);
        captured.MessageId.Should().Be(messageId);
        captured.RecordsProcessed.Should().Be(9);
        captured.RecordsCreated.Should().Be(4);
        captured.RecordsUpdated.Should().Be(2);
        captured.RecordsSkipped.Should().Be(3);
        captured.IsLastBatch.Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Terminal batch arrives before larger sibling batches -> aggregator waits
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_TerminalBatchArrivesBeforeLargerBatches_WaitsForAllBatchesBeforeFinalising()
    {
        JobRun jobRun = BuildJobRun();
        ProductsImportJob importJob = BuildImportJob(jobRun);

        List<JobRunBatchResult> storedResults = new List<JobRunBatchResult>();

        _jobRunRepo
            .Setup(r => r.GetByIdAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(jobRun);

        _jobRunRepo
            .Setup(r => r.GetProductsImportJobAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(importJob);

        _jobRunRepo
            .Setup(r => r.BatchResultExistsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns((Guid messageId, CancellationToken _) =>
                Task.FromResult(storedResults.Any(r => r.MessageId == messageId)));

        _jobRunRepo
            .Setup(r => r.AddBatchResult(It.IsAny<JobRunBatchResult>()))
            .Callback<JobRunBatchResult>(r => storedResults.Add(r));

        _jobRunRepo
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _jobRunRepo
            .Setup(r => r.UpdateAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _jobRunRepo
            .Setup(r => r.CountBatchResultsAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .Returns((Guid _, CancellationToken __) => Task.FromResult(storedResults.Count));

        _jobRunRepo
            .Setup(r => r.GetBatchResultTotalsAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .Returns((Guid _, CancellationToken __) => Task.FromResult(new JobRunBatchResultTotals
            {
                TotalProcessed = storedResults.Sum(r => r.RecordsProcessed),
                TotalCreated = storedResults.Sum(r => r.RecordsCreated),
                TotalUpdated = storedResults.Sum(r => r.RecordsUpdated),
                TotalSkipped = storedResults.Sum(r => r.RecordsSkipped),
                TotalFailed = storedResults.Sum(r => r.RecordsFailed),
                CombinedErrors = null,
            }));

        _connectionRepo
            .Setup(r => r.FindByIdAsync(jobRun.ConnectionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Connection?)null);

        _eventPublisher
            .Setup(p => p.PublishAsync(It.IsAny<ProductsImportCompletedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        ProcessProductImportBatchCompletedConsumer consumer = CreateConsumer();

        // 1) The terminal (smallest) batch finishes first: 17 cards, IsLastBatch=true,
        //    TotalBatches=3 - this is the only message that carries the total.
        await consumer.ProcessAsync(
            BuildEvent(jobRun.Id, isLastBatch: true, processed: 17, created: 17, updated: 0, skipped: 0, totalBatches: 3),
            Guid.NewGuid(),
            CancellationToken.None);

        jobRun.Status.Should().Be(JobStatus.Running, "только 1 из 3 батчей учтён - финализация преждевременна");
        _eventPublisher.Verify(
            p => p.PublishAsync(It.IsAny<ProductsImportCompletedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);

        // 2) First 50-card batch arrives.
        await consumer.ProcessAsync(
            BuildEvent(jobRun.Id, isLastBatch: false, processed: 50, created: 50, updated: 0, skipped: 0),
            Guid.NewGuid(),
            CancellationToken.None);

        jobRun.Status.Should().Be(JobStatus.Running, "учтено 2 из 3 батчей");
        _eventPublisher.Verify(
            p => p.PublishAsync(It.IsAny<ProductsImportCompletedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);

        // 3) Second (last-arriving) 50-card batch - NOT flagged IsLastBatch, yet it is the one
        //    that completes the count and must trigger finalisation.
        await consumer.ProcessAsync(
            BuildEvent(jobRun.Id, isLastBatch: false, processed: 50, created: 50, updated: 0, skipped: 0),
            Guid.NewGuid(),
            CancellationToken.None);

        jobRun.Status.Should().Be(JobStatus.Success);
        jobRun.RecordsCreated.Should().Be(117);

        _eventPublisher.Verify(
            p => p.PublishAsync(
                It.Is<ProductsImportCompletedEvent>(e =>
                    e.TotalRecordsCreated == 117),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // -----------------------------------------------------------------------
    // Concurrency guard: two batch-completion events both reach the finalisation branch
    // for the same JobRun (e.g. two consumer instances racing after both observing
    // recordedBatches == TotalImportBatches). Status is guarded by JobRun's xmin
    // concurrency token, so the loser's UpdateAsync throws DbUpdateConcurrencyException
    // instead of double-finalising / double-publishing.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_ConcurrentFinalisation_ConcurrencyConflictIsSwallowedWithoutRepublishing()
    {
        JobRun jobRun = BuildJobRun();
        ProductsImportJob importJob = BuildImportJob(jobRun);
        Guid messageId = Guid.NewGuid();

        SetupDefaults(jobRun, importJob);

        _jobRunRepo
            .Setup(r => r.BatchResultExistsAsync(messageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _jobRunRepo
            .Setup(r => r.GetBatchResultTotalsAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobRunBatchResultTotals
            {
                TotalProcessed = 1,
                TotalCreated = 1,
                TotalUpdated = 0,
                TotalSkipped = 0,
                TotalFailed = 0,
                CombinedErrors = null,
            });

        _jobRunRepo
            .Setup(r => r.CountBatchResultsAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // The loser's finalising UpdateAsync simulates losing the race.
        _jobRunRepo
            .Setup(r => r.UpdateAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateConcurrencyException());

        ProcessProductImportBatchCompletedConsumer consumer = CreateConsumer();

        Func<Task> act = () => consumer.ProcessAsync(
            BuildEvent(jobRun.Id, isLastBatch: true, processed: 1, created: 1, updated: 0, skipped: 0, totalBatches: 1),
            messageId,
            CancellationToken.None);

        await act.Should().NotThrowAsync("конфликт конкуренции должен поглощаться, а не падать наружу");

        _eventPublisher.Verify(
            p => p.PublishAsync(It.IsAny<ProductsImportCompletedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "победитель гонки публикует событие сам - проигравший не должен его дублировать");

        _connectionRepo.Verify(
            r => r.FindByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "проигравший не должен продолжать постобработку (SyncCheckpoint/Connection.LastSyncAt) после конфликта");
    }
}
