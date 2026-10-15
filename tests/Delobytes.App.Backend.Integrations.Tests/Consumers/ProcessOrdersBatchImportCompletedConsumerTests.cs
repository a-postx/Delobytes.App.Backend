using System;
using System.Collections.Generic;
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

public class ProcessOrdersBatchImportCompletedConsumerTests
{
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

    private void SetupDefaults(JobRun jobRun)
    {
        _jobRunRepo
            .Setup(r => r.GetByIdAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(jobRun);

        _jobRunRepo
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _jobRunRepo
            .Setup(r => r.UpdateAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _eventPublisher
            .Setup(p => p.PublishAsync(It.IsAny<OrdersImportCompletedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
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

        _jobRunRepo
            .Setup(r => r.BatchResultExistsAsync(messageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        JobRunBatchResultTotals totals = new JobRunBatchResultTotals
        {
            TotalProcessed = 5,
            TotalCreated = 3,
            TotalUpdated = 1,
            TotalSkipped = 1,
            TotalFailed = 0,
            CombinedErrors = null,
        };

        _jobRunRepo
            .Setup(r => r.GetBatchResultTotalsAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(totals);

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
            .Setup(r => r.BatchResultExistsAsync(messageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        JobRunBatchResultTotals totals = new JobRunBatchResultTotals
        {
            TotalProcessed = 5,
            TotalCreated = 2,
            TotalUpdated = 1,
            TotalSkipped = 0,
            TotalFailed = 2,
            CombinedErrors = "Error 1; Error 2",
        };

        _jobRunRepo
            .Setup(r => r.GetBatchResultTotalsAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(totals);

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

        _jobRunRepo
            .Setup(r => r.BatchResultExistsAsync(messageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        JobRunBatchResultTotals totals = new JobRunBatchResultTotals
        {
            TotalProcessed = 5,
            TotalCreated = 3,
            TotalUpdated = 1,
            TotalSkipped = 1,
            TotalFailed = 0,
            CombinedErrors = null,
        };

        _jobRunRepo
            .Setup(r => r.GetBatchResultTotalsAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(totals);

        _syncCheckpointRepo
            .Setup(r => r.GetAsync(jobRun.ConnectionId, JobType.OrdersImport, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncCheckpoint?)null);

        _syncCheckpointRepo
            .Setup(r => r.UpsertAsync(It.IsAny<SyncCheckpoint>(), It.IsAny<CancellationToken>()))
            .Callback<SyncCheckpoint, CancellationToken>((c, _) => savedCheckpoint = c)
            .Returns(Task.CompletedTask);

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

        ProcessOrdersBatchImportCompletedConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(BuildEvent(jobRun.Id), messageId, CancellationToken.None);

        savedCheckpoint.Should().NotBeNull();
        savedCheckpoint!.ConnectionId.Should().Be(jobRun.ConnectionId);
        savedCheckpoint.JobType.Should().Be(JobType.OrdersImport);
        savedCheckpoint.LastChangedAt.Should().NotBeNull();
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

        _jobRunRepo
            .Setup(r => r.BatchResultExistsAsync(messageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        JobRunBatchResultTotals totals = new JobRunBatchResultTotals
        {
            TotalProcessed = 10,
            TotalCreated = 6,
            TotalUpdated = 3,
            TotalSkipped = 1,
            TotalFailed = 0,
            CombinedErrors = null,
        };

        _jobRunRepo
            .Setup(r => r.GetBatchResultTotalsAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(totals);

        _syncCheckpointRepo
            .Setup(r => r.GetAsync(jobRun.ConnectionId, JobType.OrdersImport, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncCheckpoint?)null);

        _syncCheckpointRepo
            .Setup(r => r.UpsertAsync(It.IsAny<SyncCheckpoint>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

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
            .Setup(r => r.BatchResultExistsAsync(messageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        JobRunBatchResultTotals totals = new JobRunBatchResultTotals
        {
            TotalProcessed = 5,
            TotalCreated = 0,
            TotalUpdated = 0,
            TotalSkipped = 0,
            TotalFailed = 5,
            CombinedErrors = "Multiple errors",
        };

        _jobRunRepo
            .Setup(r => r.GetBatchResultTotalsAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(totals);

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
            .Setup(r => r.BatchResultExistsAsync(messageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        JobRunBatchResultTotals totals = new JobRunBatchResultTotals
        {
            TotalProcessed = 5,
            TotalCreated = 3,
            TotalUpdated = 1,
            TotalSkipped = 1,
            TotalFailed = 0,
            CombinedErrors = null,
        };

        _jobRunRepo
            .Setup(r => r.GetBatchResultTotalsAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(totals);

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
        DateTimeOffset oldWatermark = DateTimeOffset.UtcNow.AddHours(-2);
        SyncCheckpoint? savedCheckpoint = null;

        SetupDefaults(jobRun);

        _jobRunRepo
            .Setup(r => r.BatchResultExistsAsync(messageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        JobRunBatchResultTotals totals = new JobRunBatchResultTotals
        {
            TotalProcessed = 5,
            TotalCreated = 3,
            TotalUpdated = 1,
            TotalSkipped = 1,
            TotalFailed = 0,
            CombinedErrors = null,
        };

        _jobRunRepo
            .Setup(r => r.GetBatchResultTotalsAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(totals);

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

        ProcessOrdersBatchImportCompletedConsumer consumer = CreateConsumer();

        await consumer.ProcessAsync(BuildEvent(jobRun.Id), messageId, CancellationToken.None);

        savedCheckpoint.Should().NotBeNull();
        savedCheckpoint!.Id.Should().Be(existingCheckpoint.Id);
        savedCheckpoint.LastChangedAt.Should().BeAfter(oldWatermark);
        savedCheckpoint.LastSuccessAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
        savedCheckpoint.LastJobRunId.Should().Be(jobRun.Id);
    }
}
