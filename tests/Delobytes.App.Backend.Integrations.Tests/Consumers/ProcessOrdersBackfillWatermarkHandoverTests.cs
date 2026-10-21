using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Delobytes.App.Backend.Integrations.Infrastructure.Messaging;
using Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.Consumers;

/// <summary>
/// Tests the catch-up handover and delta watermarks.
/// </summary>
public class ProcessOrdersBackfillWatermarkHandoverTests
{
    /// <summary>
    /// Fixed rather than DateTimeOffset.UtcNow so assertions on the persisted watermark compare
    /// against a known value instead of whatever time the test happened to run at.
    /// </summary>
    private static readonly DateTimeOffset MaxObservedLastChangeDate = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset HandoverWatermark = MaxObservedLastChangeDate.AddHours(-1);

    private readonly Mock<IJobRunRepository> _jobRunRepo = new();
    private readonly Mock<IJobCheckpointRepository> _jobCheckpointRepo = new();
    private readonly Mock<ISyncCheckpointRepository> _syncCheckpointRepo = new();
    private readonly Mock<IConnectionRepository> _connectionRepo = new();
    private readonly Mock<IEventPublisher> _eventPublisher = new();
    private readonly Mock<ILogger<ProcessOrdersBatchImportCompletedConsumer>> _logger = new();

    private SyncCheckpoint? _savedCheckpoint;

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

    private static OrdersBatchImportCompletedEvent BuildEvent(Guid jobRunId)
    {
        return new OrdersBatchImportCompletedEvent
        {
            JobRunId = jobRunId,
            BatchId = Guid.NewGuid(),
            Created = 3,
            Updated = 1,
            Skipped = 0,
            Failed = 0,
        };
    }

    private static OrdersImportCheckpointPayload BuildPayload(
        bool isBackfill,
        bool isBackfillCompleted,
        DateTimeOffset? maxObservedLastChangeDate,
        DateTimeOffset? maxChangedAt)
    {
        return new OrdersImportCheckpointPayload
        {
            TotalBatches = 1,
            MaxChangedAt = maxChangedAt,
            WindowTo = DateTimeOffset.UtcNow,
            PagesFetched = 1,
            IsBackfill = isBackfill,
            IsBackfillCompleted = isBackfillCompleted,
            MaxObservedLastChangeDate = maxObservedLastChangeDate,
            HandoverWatermark = isBackfillCompleted ? HandoverWatermark : null,
        };
    }

    /// <summary>
    /// Arranges a run whose single batch result finalises the job, so the finalisation path — the
    /// only place the watermark is written — is actually reached.
    /// </summary>
    private void SetupFinalisingRun(
        JobRun jobRun,
        OrdersImportCheckpointPayload payload,
        SyncCheckpoint? existingCheckpoint)
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

        _jobRunRepo
            .Setup(r => r.CountBatchResultsAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _jobRunRepo
            .Setup(r => r.GetBatchResultTotalsAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobRunBatchResultTotals
            {
                TotalProcessed = 4,
                TotalCreated = 3,
                TotalUpdated = 1,
                TotalSkipped = 0,
                TotalFailed = 0,
                CombinedErrors = null,
            });

        _jobCheckpointRepo
            .Setup(r => r.GetByJobRunIdAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobCheckpoint
            {
                JobRunId = jobRun.Id,
                Payload = JsonSerializer.Serialize(payload),
                UpdatedAt = DateTimeOffset.UtcNow,
            });

        _syncCheckpointRepo
            .Setup(r => r.GetAsync(jobRun.ConnectionId, JobType.OrdersImport, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingCheckpoint);

        _syncCheckpointRepo
            .Setup(r => r.UpsertAsync(It.IsAny<SyncCheckpoint>(), It.IsAny<CancellationToken>()))
            .Callback<SyncCheckpoint, CancellationToken>((checkpoint, _) => _savedCheckpoint = checkpoint)
            .Returns(Task.CompletedTask);

        _connectionRepo
            .Setup(r => r.FindByIdAsync(jobRun.ConnectionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Connection?)null);

        _eventPublisher
            .Setup(p => p.PublishAsync(It.IsAny<OrdersImportCompletedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private static SyncCheckpoint BuildExistingCheckpoint(JobRun jobRun, DateTimeOffset lastChangedAt)
    {
        return new SyncCheckpoint
        {
            Id = Guid.NewGuid(),
            ConnectionId = jobRun.ConnectionId,
            JobType = JobType.OrdersImport,
            LastChangedAt = lastChangedAt,
            LastSuccessAt = DateTimeOffset.UtcNow.AddDays(-1),
            LastJobRunId = Guid.NewGuid(),
        };
    }

    [Fact]
    public async Task ProcessAsync_BackfillRunNotFinished_LeavesExistingWatermarkUntouched()
    {
        JobRun jobRun = BuildJobRun();
        DateTimeOffset existingWatermark = MaxObservedLastChangeDate.AddDays(-30);

        OrdersImportCheckpointPayload payload = BuildPayload(
            isBackfill: true,
            isBackfillCompleted: false,
            maxObservedLastChangeDate: MaxObservedLastChangeDate,
            maxChangedAt: null);

        SetupFinalisingRun(jobRun, payload, BuildExistingCheckpoint(jobRun, existingWatermark));

        ProcessOrdersBatchImportCompletedConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(BuildEvent(jobRun.Id), Guid.NewGuid(), CancellationToken.None);

        jobRun.Status.Should().Be(JobStatus.Success);

        // The walk is still going. Its running maximum belongs to the newest days it fetched, and
        // adopting it now would mark every older day still to come as already imported.
        _savedCheckpoint.Should().NotBeNull();
        _savedCheckpoint!.LastChangedAt.Should().Be(existingWatermark);
    }

    [Fact]
    public async Task ProcessAsync_BackfillRunNotFinished_StillRecordsSuccessAndRunId()
    {
        JobRun jobRun = BuildJobRun();
        DateTimeOffset existingWatermark = MaxObservedLastChangeDate.AddDays(-30);
        DateTimeOffset previousSuccessAt = DateTimeOffset.UtcNow.AddDays(-1);

        SyncCheckpoint existing = BuildExistingCheckpoint(jobRun, existingWatermark);
        existing.LastSuccessAt = previousSuccessAt;

        OrdersImportCheckpointPayload payload = BuildPayload(
            isBackfill: true,
            isBackfillCompleted: false,
            maxObservedLastChangeDate: MaxObservedLastChangeDate,
            maxChangedAt: null);

        SetupFinalisingRun(jobRun, payload, existing);

        ProcessOrdersBatchImportCompletedConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(BuildEvent(jobRun.Id), Guid.NewGuid(), CancellationToken.None);

        // Skipping the watermark must not be read as "this run achieved nothing": it succeeded, it
        // imported days, and the schedule needs to see that.
        _savedCheckpoint.Should().NotBeNull();
        _savedCheckpoint!.LastSuccessAt.Should().BeAfter(previousSuccessAt);
        _savedCheckpoint.LastJobRunId.Should().Be(jobRun.Id);
    }

    [Fact]
    public async Task ProcessAsync_MidWalkBackfillWithNoCheckpoint_CreatesOneWithNoWatermark()
    {
        JobRun jobRun = BuildJobRun();

        OrdersImportCheckpointPayload payload = BuildPayload(
            isBackfill: true,
            isBackfillCompleted: false,
            maxObservedLastChangeDate: MaxObservedLastChangeDate,
            maxChangedAt: null);

        SetupFinalisingRun(jobRun, payload, existingCheckpoint: null);

        ProcessOrdersBatchImportCompletedConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(BuildEvent(jobRun.Id), Guid.NewGuid(), CancellationToken.None);

        // A null watermark on a brand-new checkpoint is the "walk not handed over yet" marker the
        // delta branch reads, not a missing value to be filled with something plausible.
        _savedCheckpoint.Should().NotBeNull();
        _savedCheckpoint!.LastChangedAt.Should().BeNull();
        _savedCheckpoint.LastJobRunId.Should().Be(jobRun.Id);
        _savedCheckpoint.ConnectionId.Should().Be(jobRun.ConnectionId);
    }

    [Fact]
    public async Task ProcessAsync_BackfillRunFinishing_SeedsWatermarkFromHandover()
    {
        JobRun jobRun = BuildJobRun();
        DateTimeOffset existingWatermark = MaxObservedLastChangeDate.AddDays(-30);

        OrdersImportCheckpointPayload payload = BuildPayload(
            isBackfill: true,
            isBackfillCompleted: true,
            maxObservedLastChangeDate: MaxObservedLastChangeDate,
            maxChangedAt: null);

        SetupFinalisingRun(jobRun, payload, BuildExistingCheckpoint(jobRun, existingWatermark));

        ProcessOrdersBatchImportCompletedConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(BuildEvent(jobRun.Id), Guid.NewGuid(), CancellationToken.None);

        _savedCheckpoint.Should().NotBeNull();
        _savedCheckpoint!.LastChangedAt.Should().Be(HandoverWatermark);
    }

    [Fact]
    public async Task ProcessAsync_BackfillFinishingWithNoObservedInstant_UsesHandoverWatermark()
    {
        JobRun jobRun = BuildJobRun();

        OrdersImportCheckpointPayload payload = BuildPayload(
            isBackfill: true,
            isBackfillCompleted: true,
            maxObservedLastChangeDate: null,
            maxChangedAt: null);

        SetupFinalisingRun(jobRun, payload, BuildExistingCheckpoint(jobRun, MaxObservedLastChangeDate.AddDays(-30)));

        ProcessOrdersBatchImportCompletedConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(BuildEvent(jobRun.Id), Guid.NewGuid(), CancellationToken.None);

        _savedCheckpoint.Should().NotBeNull();
        _savedCheckpoint!.LastChangedAt.Should().Be(HandoverWatermark);
    }

    [Fact]
    public async Task ProcessAsync_DeltaRun_WritesWatermarkFromPayload()
    {
        JobRun jobRun = BuildJobRun();
        DateTimeOffset previousWatermark = MaxObservedLastChangeDate.AddDays(-30);

        OrdersImportCheckpointPayload payload = BuildPayload(
            isBackfill: false,
            isBackfillCompleted: false,
            maxObservedLastChangeDate: null,
            maxChangedAt: MaxObservedLastChangeDate);

        SetupFinalisingRun(jobRun, payload, BuildExistingCheckpoint(jobRun, previousWatermark));

        ProcessOrdersBatchImportCompletedConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(BuildEvent(jobRun.Id), Guid.NewGuid(), CancellationToken.None);

        // Regression guard: the delta branch must keep writing exactly what the orchestrator
        // observed, and must not consult any backfill field.
        _savedCheckpoint.Should().NotBeNull();
        _savedCheckpoint!.LastChangedAt.Should().Be(MaxObservedLastChangeDate);
    }

    [Fact]
    public async Task ProcessAsync_DeltaRunFetchedNothing_KeepsPreviousWatermark()
    {
        JobRun jobRun = BuildJobRun();
        DateTimeOffset previousWatermark = MaxObservedLastChangeDate.AddDays(-30);

        OrdersImportCheckpointPayload payload = BuildPayload(
            isBackfill: false,
            isBackfillCompleted: false,
            maxObservedLastChangeDate: null,
            maxChangedAt: null);

        SetupFinalisingRun(jobRun, payload, BuildExistingCheckpoint(jobRun, previousWatermark));

        ProcessOrdersBatchImportCompletedConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(BuildEvent(jobRun.Id), Guid.NewGuid(), CancellationToken.None);

        _savedCheckpoint.Should().NotBeNull();
        _savedCheckpoint!.LastChangedAt.Should().Be(previousWatermark);
        _savedCheckpoint.LastJobRunId.Should().Be(jobRun.Id);
    }

    [Fact]
    public async Task ProcessAsync_BackfillBranchIsTakenFromPayload_NotFromState()
    {
        JobRun jobRun = BuildJobRun();
        DateTimeOffset existingWatermark = MaxObservedLastChangeDate.AddDays(-30);

        // The payload says the walk is still running while the running maximum is already known.
        // The aggregator must judge the run by the branch it took, never by a cursor row the
        // orchestrator rewrites only after the batches of the run are published.
        OrdersImportCheckpointPayload payload = BuildPayload(
            isBackfill: true,
            isBackfillCompleted: false,
            maxObservedLastChangeDate: MaxObservedLastChangeDate,
            maxChangedAt: null);

        SetupFinalisingRun(jobRun, payload, BuildExistingCheckpoint(jobRun, existingWatermark));

        ProcessOrdersBatchImportCompletedConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(BuildEvent(jobRun.Id), Guid.NewGuid(), CancellationToken.None);

        _savedCheckpoint.Should().NotBeNull();
        _savedCheckpoint!.LastChangedAt.Should().Be(existingWatermark);
    }

    [Fact]
    public async Task ProcessAsync_RunFailed_WritesNoWatermark()
    {
        JobRun jobRun = BuildJobRun();

        OrdersImportCheckpointPayload payload = BuildPayload(
            isBackfill: true,
            isBackfillCompleted: true,
            maxObservedLastChangeDate: MaxObservedLastChangeDate,
            maxChangedAt: null);

        SetupFinalisingRun(jobRun, payload, BuildExistingCheckpoint(jobRun, MaxObservedLastChangeDate.AddDays(-30)));

        _jobRunRepo
            .Setup(r => r.GetBatchResultTotalsAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobRunBatchResultTotals
            {
                TotalProcessed = 1,
                TotalCreated = 0,
                TotalUpdated = 0,
                TotalSkipped = 0,
                TotalFailed = 1,
                CombinedErrors = "one order was rejected",
            });

        ProcessOrdersBatchImportCompletedConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(BuildEvent(jobRun.Id), Guid.NewGuid(), CancellationToken.None);

        // A failed run imports nothing trustworthy, so it may not hand a watermark forward even if
        // it happens to be the run that reached the end of the walk.
        jobRun.Status.Should().Be(JobStatus.Failed);

        _savedCheckpoint.Should().BeNull();
        _syncCheckpointRepo.Verify(
            r => r.UpsertAsync(It.IsAny<SyncCheckpoint>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_WalkFinishes_NextRunTakesDeltaBranchFromTheSeededWatermark()
    {
        JobRun jobRun = BuildJobRun();

        OrdersImportCheckpointPayload payload = BuildPayload(
            isBackfill: true,
            isBackfillCompleted: true,
            maxObservedLastChangeDate: MaxObservedLastChangeDate,
            maxChangedAt: null);

        SetupFinalisingRun(jobRun, payload, BuildExistingCheckpoint(jobRun, MaxObservedLastChangeDate.AddDays(-30)));

        ProcessOrdersBatchImportCompletedConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(BuildEvent(jobRun.Id), Guid.NewGuid(), CancellationToken.None);

        _savedCheckpoint.Should().NotBeNull();

        DateTimeOffset expectedWindowFrom = _savedCheckpoint!.LastChangedAt!.Value
            .Subtract(ProcessOrdersImportConsumer.SafetyOverlap);

        _savedCheckpoint!.LastChangedAt.Should().Be(HandoverWatermark);
        expectedWindowFrom.Should().Be(HandoverWatermark.Subtract(ProcessOrdersImportConsumer.SafetyOverlap));
        expectedWindowFrom.Should().BeAfter(MaxObservedLastChangeDate.AddDays(-1));
    }
}
