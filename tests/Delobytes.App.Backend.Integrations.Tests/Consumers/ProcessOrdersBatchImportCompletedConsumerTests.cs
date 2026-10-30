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
using Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers.OrdersImport;
using FluentAssertions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.Consumers;

/// <summary>
/// Tests for the orders batch-completed aggregator. Covers idempotent accumulation, the
/// TotalBatches gate that prevents finalising a multi-batch run on its first result, watermark
/// handling, and the MassTransit adapter's use of the batch's deterministic BatchId (not the
/// transport MessageId) as the idempotency key.
/// </summary>
public partial class ProcessOrdersBatchImportCompletedConsumerTests
{
    /// <summary>
    /// Fixed rather than DateTimeOffset.UtcNow, so assertions on the persisted watermark compare
    /// against a known value instead of "whatever time the test happened to run at".
    /// </summary>
    private static readonly DateTimeOffset FetchedMaxChangedAt = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<IJobRunRepository> _jobRunRepo = new();
    private readonly Mock<IJobCheckpointRepository> _jobCheckpointRepo = new();
    private readonly Mock<ISyncCheckpointRepository> _syncCheckpointRepo = new();
    private readonly Mock<IConnectionRepository> _connectionRepo = new();
    private readonly Mock<IEventPublisher> _eventPublisher = new();
    private readonly Mock<ILogger<ProcessOrdersBatchImportCompletedConsumer>> _logger = new();

    private ProcessOrdersBatchImportCompletedConsumer CreateConsumer()
    {
        return new ProcessOrdersBatchImportCompletedConsumer(
            _jobRunRepo.Object,
            _jobCheckpointRepo.Object,
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
            JobType = JobType.OrdersImport,
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    private static OrdersBatchImportCompletedEvent BuildEvent(
        Guid jobRunId,
        int created = 3,
        int updated = 1,
        int skipped = 1,
        int failed = 0)
    {
        return new OrdersBatchImportCompletedEvent
        {
            JobRunId = jobRunId,
            BatchId = Guid.NewGuid(),
            Created = created,
            Updated = updated,
            Skipped = skipped,
            Failed = failed,
        };
    }

    private static JobRunBatchResultTotals BuildTotals(int created, int updated, int skipped, int failed, string? errors = null)
    {
        return new JobRunBatchResultTotals
        {
            TotalProcessed = created + updated + skipped + failed,
            TotalCreated = created,
            TotalUpdated = updated,
            TotalSkipped = skipped,
            TotalFailed = failed,
            CombinedErrors = errors,
        };
    }

    /// <summary>
    /// Arranges the orchestrator's persisted expectations (JobCheckpoint.Payload) and how many
    /// batch results are already stored, which together decide whether the aggregator finalises
    /// the run on this call. Defaults to a single-batch run (1 of 1) so every pre-existing test
    /// below, written when the aggregator finalised unconditionally, keeps the same outcome.
    /// </summary>
    private void SetupDefaults(JobRun jobRun, int totalBatches = 1, int recordedBatches = 1)
    {
        _jobRunRepo
            .Setup(r => r.GetByIdAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(jobRun);

        _jobRunRepo
            .Setup(r => r.BatchResultExistsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _jobRunRepo
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _jobRunRepo
            .Setup(r => r.UpdateAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _eventPublisher
            .Setup(p => p.PublishAsync(It.IsAny<OrdersImportCompletedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // The aggregator reads the expected batch count from the checkpoint the orchestrator
        // persisted before publishing its first batch (see ProcessOrdersImportConsumer). Without
        // stubbing this, GetByJobRunIdAsync resolves to null, the aggregator cannot tell whether
        // this is the last batch, and it correctly leaves the JobRun Running.
        _jobCheckpointRepo
            .Setup(r => r.GetByJobRunIdAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobCheckpoint
            {
                JobRunId = jobRun.Id,
                Payload = JsonSerializer.Serialize(new OrdersImportCheckpointPayload
                {
                    TotalBatches = totalBatches,
                    MaxChangedAt = FetchedMaxChangedAt,
                    WindowTo = DateTimeOffset.UtcNow,
                    PagesFetched = 1,
                }),
                UpdatedAt = DateTimeOffset.UtcNow,
            });

        _jobRunRepo
            .Setup(r => r.CountBatchResultsAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(recordedBatches);
    }

    private Connection SetupConnection(JobRun jobRun)
    {
        Connection connection = new Connection
        {
            Id = jobRun.ConnectionId,
            ChannelId = Guid.NewGuid(),
            Name = "Test",
            ApiKey = "key",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _connectionRepo
            .Setup(r => r.FindByIdAsync(jobRun.ConnectionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);

        _connectionRepo
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        return connection;
    }

    [Fact]
    public async Task ProcessAsync_JobRunNotFound_SkipsWithoutAction()
    {
        Guid jobRunId = Guid.NewGuid();

        _jobRunRepo
            .Setup(r => r.GetByIdAsync(jobRunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((JobRun?)null);

        ProcessOrdersBatchImportCompletedConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(BuildEvent(jobRunId), Guid.NewGuid(), CancellationToken.None);

        _jobRunRepo.Verify(r => r.AddBatchResult(It.IsAny<JobRunBatchResult>()), Times.Never);
        _eventPublisher.Verify(
            p => p.PublishAsync(It.IsAny<OrdersImportCompletedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

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

        ProcessOrdersBatchImportCompletedConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(BuildEvent(jobRun.Id), messageId, CancellationToken.None);

        _jobRunRepo.Verify(r => r.AddBatchResult(It.IsAny<JobRunBatchResult>()), Times.Never);
        _jobRunRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_DuplicateLastBatchRedeliveryAfterTerminal_RepublishesCompletedEvent()
    {
        JobRun jobRun = BuildJobRun(JobStatus.Success);
        jobRun.CompletedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        Guid messageId = Guid.NewGuid();

        _jobRunRepo
            .Setup(r => r.GetByIdAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(jobRun);

        _jobRunRepo
            .Setup(r => r.BatchResultExistsAsync(messageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _eventPublisher
            .Setup(p => p.PublishAsync(It.IsAny<OrdersImportCompletedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        ProcessOrdersBatchImportCompletedConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(BuildEvent(jobRun.Id), messageId, CancellationToken.None);

        _eventPublisher.Verify(
            p => p.PublishAsync(
                It.Is<OrdersImportCompletedEvent>(e => e.JobRunId == jobRun.Id),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_FirstBatch_RecordsBatchResultAndAccumulatesCounters()
    {
        JobRun jobRun = BuildJobRun();
        Guid messageId = Guid.NewGuid();

        SetupDefaults(jobRun);
        SetupConnection(jobRun);

        _jobRunRepo
            .Setup(r => r.GetBatchResultTotalsAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildTotals(created: 3, updated: 1, skipped: 1, failed: 0));

        _syncCheckpointRepo
            .Setup(r => r.GetAsync(jobRun.ConnectionId, JobType.OrdersImport, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncCheckpoint?)null);

        _syncCheckpointRepo
            .Setup(r => r.UpsertAsync(It.IsAny<SyncCheckpoint>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        ProcessOrdersBatchImportCompletedConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(
            BuildEvent(jobRun.Id, created: 3, updated: 1, skipped: 1, failed: 0),
            messageId,
            CancellationToken.None);

        _jobRunRepo.Verify(r => r.AddBatchResult(It.IsAny<JobRunBatchResult>()), Times.Once);
        _jobRunRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);

        jobRun.RecordsCreated.Should().Be(3);
        jobRun.RecordsUpdated.Should().Be(1);
        jobRun.RecordsSkipped.Should().Be(1);
        jobRun.RecordsFailed.Should().Be(0);
        jobRun.Status.Should().Be(JobStatus.Success);
        jobRun.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ProcessAsync_BatchWithErrors_FinalisesAsFailedWithCombinedErrors()
    {
        JobRun jobRun = BuildJobRun();
        Guid messageId = Guid.NewGuid();

        SetupDefaults(jobRun);

        _jobRunRepo
            .Setup(r => r.GetBatchResultTotalsAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildTotals(created: 2, updated: 1, skipped: 0, failed: 2, errors: "Error 1; Error 2"));

        ProcessOrdersBatchImportCompletedConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(
            BuildEvent(jobRun.Id, created: 2, updated: 1, skipped: 0, failed: 2),
            messageId,
            CancellationToken.None);

        jobRun.Status.Should().Be(JobStatus.Failed);
        jobRun.Error.Should().Be("Error 1; Error 2");
        jobRun.RecordsFailed.Should().Be(2);
        jobRun.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ProcessAsync_SuccessfulCompletion_UpdatesSyncCheckpoint()
    {
        JobRun jobRun = BuildJobRun();
        Guid messageId = Guid.NewGuid();
        SyncCheckpoint? savedCheckpoint = null;

        SetupDefaults(jobRun);
        Connection connection = SetupConnection(jobRun);

        _jobRunRepo
            .Setup(r => r.GetBatchResultTotalsAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildTotals(created: 3, updated: 1, skipped: 1, failed: 0));

        _syncCheckpointRepo
            .Setup(r => r.GetAsync(jobRun.ConnectionId, JobType.OrdersImport, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncCheckpoint?)null);

        _syncCheckpointRepo
            .Setup(r => r.UpsertAsync(It.IsAny<SyncCheckpoint>(), It.IsAny<CancellationToken>()))
            .Callback<SyncCheckpoint, CancellationToken>((c, _) => savedCheckpoint = c)
            .Returns(Task.CompletedTask);

        ProcessOrdersBatchImportCompletedConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(BuildEvent(jobRun.Id), messageId, CancellationToken.None);

        savedCheckpoint.Should().NotBeNull();
        savedCheckpoint!.ConnectionId.Should().Be(jobRun.ConnectionId);
        savedCheckpoint.JobType.Should().Be(JobType.OrdersImport);
        savedCheckpoint.LastChangedAt.Should().Be(FetchedMaxChangedAt);
        savedCheckpoint.LastSuccessAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
        savedCheckpoint.LastJobRunId.Should().Be(jobRun.Id);

        connection.LastSyncAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ProcessAsync_SuccessfulCompletion_PublishesOrdersImportCompletedEvent()
    {
        JobRun jobRun = BuildJobRun();
        Guid messageId = Guid.NewGuid();
        OrdersImportCompletedEvent? publishedEvent = null;

        SetupDefaults(jobRun);
        SetupConnection(jobRun);

        _jobRunRepo
            .Setup(r => r.GetBatchResultTotalsAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildTotals(created: 6, updated: 3, skipped: 1, failed: 0));

        _syncCheckpointRepo
            .Setup(r => r.GetAsync(jobRun.ConnectionId, JobType.OrdersImport, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncCheckpoint?)null);

        _syncCheckpointRepo
            .Setup(r => r.UpsertAsync(It.IsAny<SyncCheckpoint>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _eventPublisher
            .Setup(p => p.PublishAsync(It.IsAny<OrdersImportCompletedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<OrdersImportCompletedEvent, CancellationToken>((e, _) => publishedEvent = e)
            .Returns(Task.CompletedTask);

        ProcessOrdersBatchImportCompletedConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(
            BuildEvent(jobRun.Id, created: 6, updated: 3, skipped: 1, failed: 0),
            messageId,
            CancellationToken.None);

        publishedEvent.Should().NotBeNull();
        publishedEvent!.JobRunId.Should().Be(jobRun.Id);
        publishedEvent.TotalCreated.Should().Be(6);
        publishedEvent.TotalUpdated.Should().Be(3);
        publishedEvent.Status.Should().Be(JobStatus.Success);
    }

    [Fact]
    public async Task ProcessAsync_FailedImport_DoesNotUpdateSyncCheckpoint()
    {
        JobRun jobRun = BuildJobRun();
        Guid messageId = Guid.NewGuid();

        SetupDefaults(jobRun);

        _jobRunRepo
            .Setup(r => r.GetBatchResultTotalsAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildTotals(created: 0, updated: 0, skipped: 0, failed: 5, errors: "Multiple errors"));

        ProcessOrdersBatchImportCompletedConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(
            BuildEvent(jobRun.Id, created: 0, updated: 0, skipped: 0, failed: 5),
            messageId,
            CancellationToken.None);

        jobRun.Status.Should().Be(JobStatus.Failed);

        _syncCheckpointRepo.Verify(
            r => r.UpsertAsync(It.IsAny<SyncCheckpoint>(), It.IsAny<CancellationToken>()),
            Times.Never);

        _connectionRepo.Verify(
            r => r.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_ConcurrentFinalisation_HandlesDbUpdateConcurrencyException()
    {
        JobRun jobRun = BuildJobRun();
        Guid messageId = Guid.NewGuid();

        SetupDefaults(jobRun);

        _jobRunRepo
            .Setup(r => r.GetBatchResultTotalsAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildTotals(created: 3, updated: 1, skipped: 1, failed: 0));

        _jobRunRepo
            .Setup(r => r.UpdateAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateConcurrencyException());

        ProcessOrdersBatchImportCompletedConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(BuildEvent(jobRun.Id), messageId, CancellationToken.None);

        _syncCheckpointRepo.Verify(
            r => r.UpsertAsync(It.IsAny<SyncCheckpoint>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "checkpoint update is skipped when concurrent finalisation is detected");

        _eventPublisher.Verify(
            p => p.PublishAsync(It.IsAny<OrdersImportCompletedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "completed event is skipped when concurrent finalisation is detected");
    }

    [Fact]
    public async Task ProcessAsync_ExistingCheckpoint_UpdatesWatermark()
    {
        JobRun jobRun = BuildJobRun();
        Guid messageId = Guid.NewGuid();
        DateTimeOffset oldWatermark = FetchedMaxChangedAt.AddHours(-2);
        SyncCheckpoint? savedCheckpoint = null;

        SetupDefaults(jobRun);
        SetupConnection(jobRun);

        _jobRunRepo
            .Setup(r => r.GetBatchResultTotalsAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildTotals(created: 3, updated: 1, skipped: 1, failed: 0));

        SyncCheckpoint existingCheckpoint = new SyncCheckpoint
        {
            Id = Guid.NewGuid(),
            ConnectionId = jobRun.ConnectionId,
            JobType = JobType.OrdersImport,
            LastChangedAt = oldWatermark,
            LastSuccessAt = DateTimeOffset.UtcNow.AddHours(-2),
            LastJobRunId = Guid.NewGuid(),
        };

        _syncCheckpointRepo
            .Setup(r => r.GetAsync(jobRun.ConnectionId, JobType.OrdersImport, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingCheckpoint);

        _syncCheckpointRepo
            .Setup(r => r.UpsertAsync(It.IsAny<SyncCheckpoint>(), It.IsAny<CancellationToken>()))
            .Callback<SyncCheckpoint, CancellationToken>((c, _) => savedCheckpoint = c)
            .Returns(Task.CompletedTask);

        ProcessOrdersBatchImportCompletedConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(BuildEvent(jobRun.Id), messageId, CancellationToken.None);

        savedCheckpoint.Should().NotBeNull();
        savedCheckpoint!.Id.Should().Be(existingCheckpoint.Id);
        savedCheckpoint.LastChangedAt.Should().Be(FetchedMaxChangedAt);
        savedCheckpoint.LastChangedAt.Should().BeAfter(oldWatermark);
        savedCheckpoint.LastSuccessAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
        savedCheckpoint.LastJobRunId.Should().Be(jobRun.Id);
    }

    [Fact]
    public async Task ProcessAsync_NotAllBatchesRecorded_DoesNotFinaliseJobRun()
    {
        // Regression test for the core defect: the aggregator used to finalise on the very first
        // batch result regardless of how many batches the orchestrator actually announced.
        JobRun jobRun = BuildJobRun();

        SetupDefaults(jobRun, totalBatches: 3, recordedBatches: 1);

        ProcessOrdersBatchImportCompletedConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(BuildEvent(jobRun.Id), Guid.NewGuid(), CancellationToken.None);

        _jobRunRepo.Verify(r => r.AddBatchResult(It.IsAny<JobRunBatchResult>()), Times.Once);

        jobRun.Status.Should().Be(JobStatus.Running);
        jobRun.CompletedAt.Should().BeNull();
        jobRun.RecordsCreated.Should().Be(0, "counters are an aggregate over every batch and must not be published partially");

        _jobRunRepo.Verify(r => r.UpdateAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()), Times.Never);
        _jobRunRepo.Verify(r => r.GetBatchResultTotalsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _syncCheckpointRepo.Verify(r => r.UpsertAsync(It.IsAny<SyncCheckpoint>(), It.IsAny<CancellationToken>()), Times.Never);
        _eventPublisher.Verify(
            p => p.PublishAsync(It.IsAny<OrdersImportCompletedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_ThreeBatches_FinalisesOnceOnLastRecordedResult()
    {
        JobRun jobRun = BuildJobRun();
        int recordedBatches = 0;

        SetupDefaults(jobRun, totalBatches: 3, recordedBatches: 0);
        SetupConnection(jobRun);

        // CountBatchResultsAsync must reflect the store's state at the moment each call checks it,
        // not a value fixed up front, otherwise this test could not tell "batch 2 of 3" from
        // "batch 3 of 3" the way the real repository does.
        _jobRunRepo
            .Setup(r => r.AddBatchResult(It.IsAny<JobRunBatchResult>()))
            .Callback(() => recordedBatches++);

        _jobRunRepo
            .Setup(r => r.CountBatchResultsAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => recordedBatches);

        _jobRunRepo
            .Setup(r => r.GetBatchResultTotalsAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildTotals(created: 25, updated: 0, skipped: 0, failed: 0));

        _syncCheckpointRepo
            .Setup(r => r.GetAsync(jobRun.ConnectionId, JobType.OrdersImport, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncCheckpoint?)null);

        _syncCheckpointRepo
            .Setup(r => r.UpsertAsync(It.IsAny<SyncCheckpoint>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        ProcessOrdersBatchImportCompletedConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(BuildEvent(jobRun.Id, created: 10, updated: 0, skipped: 0), Guid.NewGuid(), CancellationToken.None);
        jobRun.Status.Should().Be(JobStatus.Running, "batch 1 of 3");

        await consumer.ProcessAsync(BuildEvent(jobRun.Id, created: 10, updated: 0, skipped: 0), Guid.NewGuid(), CancellationToken.None);
        jobRun.Status.Should().Be(JobStatus.Running, "batch 2 of 3");

        await consumer.ProcessAsync(BuildEvent(jobRun.Id, created: 5, updated: 0, skipped: 0), Guid.NewGuid(), CancellationToken.None);

        jobRun.Status.Should().Be(JobStatus.Success);
        jobRun.RecordsCreated.Should().Be(25);
        jobRun.CompletedAt.Should().NotBeNull();

        _jobRunRepo.Verify(r => r.UpdateAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()), Times.Once);
        _syncCheckpointRepo.Verify(r => r.UpsertAsync(It.IsAny<SyncCheckpoint>(), It.IsAny<CancellationToken>()), Times.Once);
        _eventPublisher.Verify(
            p => p.PublishAsync(It.IsAny<OrdersImportCompletedEvent>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_CheckpointMissing_DoesNotFinaliseJobRun()
    {
        JobRun jobRun = BuildJobRun();

        SetupDefaults(jobRun);

        // Overrides the default stub from SetupDefaults: no checkpoint row exists at all, e.g.
        // because the orchestrator crashed before persisting it.
        _jobCheckpointRepo
            .Setup(r => r.GetByJobRunIdAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((JobCheckpoint?)null);

        ProcessOrdersBatchImportCompletedConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(BuildEvent(jobRun.Id), Guid.NewGuid(), CancellationToken.None);

        _jobRunRepo.Verify(r => r.AddBatchResult(It.IsAny<JobRunBatchResult>()), Times.Once);
        jobRun.Status.Should().Be(JobStatus.Running);
        _jobRunRepo.Verify(r => r.UpdateAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()), Times.Never);
        _eventPublisher.Verify(
            p => p.PublishAsync(It.IsAny<OrdersImportCompletedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_UnreadableCheckpointPayload_DoesNotFinaliseJobRun()
    {
        JobRun jobRun = BuildJobRun();

        SetupDefaults(jobRun);

        _jobCheckpointRepo
            .Setup(r => r.GetByJobRunIdAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobCheckpoint
            {
                JobRunId = jobRun.Id,
                Payload = "{not json",
                UpdatedAt = DateTimeOffset.UtcNow,
            });

        ProcessOrdersBatchImportCompletedConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(BuildEvent(jobRun.Id), Guid.NewGuid(), CancellationToken.None);

        jobRun.Status.Should().Be(JobStatus.Running);
        _jobRunRepo.Verify(r => r.UpdateAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_FetchedNothing_KeepsPreviousWatermark()
    {
        JobRun jobRun = BuildJobRun();
        DateTimeOffset previousWatermark = FetchedMaxChangedAt.AddHours(-2);
        SyncCheckpoint? savedCheckpoint = null;

        _jobRunRepo
            .Setup(r => r.GetByIdAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(jobRun);

        _jobRunRepo
            .Setup(r => r.BatchResultExistsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _jobRunRepo
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _jobRunRepo
            .Setup(r => r.UpdateAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _eventPublisher
            .Setup(p => p.PublishAsync(It.IsAny<OrdersImportCompletedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // MaxChangedAt=null models a delta run whose window genuinely contained no orders.
        OrdersImportCheckpointPayload payload = new OrdersImportCheckpointPayload
        {
            TotalBatches = 1,
            MaxChangedAt = null,
            WindowTo = DateTimeOffset.UtcNow,
            PagesFetched = 1,
            IsFetchCompleted = true,
        };

        _jobCheckpointRepo
            .Setup(r => r.GetByJobRunIdAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobCheckpoint
            {
                JobRunId = jobRun.Id,
                Payload = JsonSerializer.Serialize(payload),
                UpdatedAt = DateTimeOffset.UtcNow,
            });

        _jobRunRepo
            .Setup(r => r.CountBatchResultsAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _jobRunRepo
            .Setup(r => r.GetBatchResultTotalsAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildTotals(created: 0, updated: 0, skipped: 0, failed: 0));

        SyncCheckpoint existingCheckpoint = new SyncCheckpoint
        {
            Id = Guid.NewGuid(),
            ConnectionId = jobRun.ConnectionId,
            JobType = JobType.OrdersImport,
            LastChangedAt = previousWatermark,
            LastSuccessAt = DateTimeOffset.UtcNow.AddHours(-2),
            LastJobRunId = Guid.NewGuid(),
        };

        _syncCheckpointRepo
            .Setup(r => r.GetAsync(jobRun.ConnectionId, JobType.OrdersImport, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingCheckpoint);

        _syncCheckpointRepo
            .Setup(r => r.UpsertAsync(It.IsAny<SyncCheckpoint>(), It.IsAny<CancellationToken>()))
            .Callback<SyncCheckpoint, CancellationToken>((c, _) => savedCheckpoint = c)
            .Returns(Task.CompletedTask);

        ProcessOrdersBatchImportCompletedConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(BuildEvent(jobRun.Id, created: 0, updated: 0, skipped: 0), Guid.NewGuid(), CancellationToken.None);

        jobRun.Status.Should().Be(JobStatus.Success);
        savedCheckpoint.Should().NotBeNull();
        savedCheckpoint!.LastChangedAt.Should().Be(previousWatermark);
        savedCheckpoint.LastJobRunId.Should().Be(jobRun.Id);
    }

    [Fact]
    public async Task MassTransitAdapter_UsesBatchIdAsIdempotencyKey_NotTransportMessageId()
    {
        // Regression test: the adapter previously passed context.MessageId (fresh on every
        // redelivery) instead of context.Message.BatchId (deterministic per JobRun+batch index).
        // With MessageId, a redelivered batch would be recorded as an extra, distinct result and
        // could push CountBatchResultsAsync up to TotalBatches before the real last batch arrived.
        JobRun jobRun = BuildJobRun();
        Guid batchId = Guid.NewGuid();
        Guid transportMessageId = Guid.NewGuid();

        _jobRunRepo
            .Setup(r => r.GetByIdAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(jobRun);

        _jobRunRepo
            .Setup(r => r.BatchResultExistsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _jobCheckpointRepo
            .Setup(r => r.GetByJobRunIdAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((JobCheckpoint?)null);

        JobRunBatchResult? recorded = null;

        _jobRunRepo
            .Setup(r => r.AddBatchResult(It.IsAny<JobRunBatchResult>()))
            .Callback<JobRunBatchResult>(r => recorded = r);

        OrdersBatchImportCompletedEvent message = BuildEvent(jobRun.Id) with { BatchId = batchId };

        Mock<ConsumeContext<OrdersBatchImportCompletedEvent>> context = new Mock<ConsumeContext<OrdersBatchImportCompletedEvent>>();
        context.SetupGet(c => c.Message).Returns(message);
        context.SetupGet(c => c.MessageId).Returns(transportMessageId);
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);

        ProcessOrdersBatchImportCompletedMassTransitConsumer adapter =
            new ProcessOrdersBatchImportCompletedMassTransitConsumer(CreateConsumer());

        await adapter.Consume(context.Object);

        _jobRunRepo.Verify(r => r.BatchResultExistsAsync(batchId, It.IsAny<CancellationToken>()), Times.Once);
        _jobRunRepo.Verify(r => r.BatchResultExistsAsync(transportMessageId, It.IsAny<CancellationToken>()), Times.Never);
        recorded.Should().NotBeNull();
        recorded!.MessageId.Should().Be(batchId);
    }
}
