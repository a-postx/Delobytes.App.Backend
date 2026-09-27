using System;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers;
using FluentAssertions;
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
        string? errorMessage = null)
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

        _syncJobRepo.Verify(r => r.Update(syncJob), Times.Once);
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
}
