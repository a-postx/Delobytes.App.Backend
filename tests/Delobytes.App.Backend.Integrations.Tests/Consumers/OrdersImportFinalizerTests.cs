using System;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Orders;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.Consumers;

/// <summary>
/// Tests the three pieces the batch-completed consumer delegates to, with the run's clock and the
/// concurrency token under the test's control.
/// </summary>
public class OrdersImportFinalizerTests
{
    private static readonly DateTimeOffset FinalisedAt = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<IJobRunRepository> _jobRunRepo = new();
    private readonly Mock<ISyncCheckpointRepository> _syncCheckpointRepo = new();
    private readonly Mock<IConnectionRepository> _connectionRepo = new();
    private readonly FakeTimeProvider _timeProvider = new(FinalisedAt);

    [Fact]
    public async Task FinalizeAsync_Success_WritesCountersAndDeterministicCompletionTime()
    {
        JobRun jobRun = BuildJobRun();
        SetupTotals(jobRun, created: 5, updated: 2, skipped: 1, failed: 0);
        _jobRunRepo
            .Setup(r => r.TryUpdateAsync(jobRun, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        OrdersImportFinalizationResult result = await CreateFinalizer().FinalizeAsync(jobRun, CancellationToken.None);

        result.Should().Be(OrdersImportFinalizationResult.Finalized);
        jobRun.Status.Should().Be(JobStatus.Success);
        jobRun.RecordsCreated.Should().Be(5);
        jobRun.RecordsUpdated.Should().Be(2);
        jobRun.RecordsSkipped.Should().Be(1);
        jobRun.CompletedAt.Should().Be(FinalisedAt);
    }

    [Fact]
    public async Task FinalizeAsync_AnyFailedRecord_MarksTheRunFailedWithCombinedErrors()
    {
        JobRun jobRun = BuildJobRun();
        SetupTotals(jobRun, created: 2, updated: 0, skipped: 0, failed: 1, errors: "batch 3 rejected");
        _jobRunRepo
            .Setup(r => r.TryUpdateAsync(jobRun, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await CreateFinalizer().FinalizeAsync(jobRun, CancellationToken.None);

        jobRun.Status.Should().Be(JobStatus.Failed);
        jobRun.Error.Should().Be("batch 3 rejected");
    }

    [Fact]
    public async Task FinalizeAsync_ConcurrencyConflict_ReturnsAlreadyFinalized()
    {
        JobRun jobRun = BuildJobRun();
        SetupTotals(jobRun, created: 1, updated: 0, skipped: 0, failed: 0);
        _jobRunRepo
            .Setup(r => r.TryUpdateAsync(jobRun, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        OrdersImportFinalizationResult result = await CreateFinalizer().FinalizeAsync(jobRun, CancellationToken.None);

        result.Should().Be(OrdersImportFinalizationResult.AlreadyFinalized);
    }

    [Fact]
    public async Task WriteAsync_NonSuccessRun_WritesNoWatermark()
    {
        JobRun jobRun = BuildJobRun();
        jobRun.Status = JobStatus.Failed;

        await CreateWatermarkWriter().WriteAsync(jobRun, BuildPayload(), CancellationToken.None);

        _syncCheckpointRepo.Verify(
            r => r.UpsertAsync(It.IsAny<SyncCheckpoint>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _connectionRepo.Verify(
            r => r.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task WriteAsync_SuccessfulDeltaRun_WritesWatermarkFromPayload()
    {
        JobRun jobRun = BuildJobRun();
        jobRun.Status = JobStatus.Success;
        SyncCheckpoint? saved = null;

        _syncCheckpointRepo
            .Setup(r => r.GetAsync(jobRun.ConnectionId, jobRun.JobType, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncCheckpoint?)null);
        _syncCheckpointRepo
            .Setup(r => r.UpsertAsync(It.IsAny<SyncCheckpoint>(), It.IsAny<CancellationToken>()))
            .Callback<SyncCheckpoint, CancellationToken>((checkpoint, _) => saved = checkpoint)
            .Returns(Task.CompletedTask);
        _connectionRepo
            .Setup(r => r.FindByIdAsync(jobRun.ConnectionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Connection?)null);

        await CreateWatermarkWriter().WriteAsync(jobRun, BuildPayload(), CancellationToken.None);

        saved.Should().NotBeNull();
        saved!.LastChangedAt.Should().Be(FinalisedAt.AddMinutes(-5));
        saved.LastSuccessAt.Should().Be(FinalisedAt);
        saved.LastJobRunId.Should().Be(jobRun.Id);
    }

    private OrdersImportFinalizer CreateFinalizer()
    {
        return new OrdersImportFinalizer(
            _jobRunRepo.Object,
            _timeProvider,
            NullLogger<OrdersImportFinalizer>.Instance);
    }

    private OrdersImportWatermarkWriter CreateWatermarkWriter()
    {
        return new OrdersImportWatermarkWriter(
            _syncCheckpointRepo.Object,
            _connectionRepo.Object,
            _timeProvider,
            NullLogger<OrdersImportWatermarkWriter>.Instance);
    }

    private void SetupTotals(JobRun jobRun, int created, int updated, int skipped, int failed, string? errors = null)
    {
        _jobRunRepo
            .Setup(r => r.GetBatchResultTotalsAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobRunBatchResultTotals
            {
                TotalProcessed = created + updated + skipped + failed,
                TotalCreated = created,
                TotalUpdated = updated,
                TotalSkipped = skipped,
                TotalFailed = failed,
                CombinedErrors = errors,
            });
    }

    private static JobRun BuildJobRun()
    {
        return new JobRun
        {
            Id = Guid.NewGuid(),
            ConnectionId = Guid.NewGuid(),
            JobType = JobType.WbOrdersImport,
            Status = JobStatus.Running,
            CreatedAt = FinalisedAt.AddHours(-1),
        };
    }

    private static OrdersImportCheckpointPayload BuildPayload()
    {
        return new OrdersImportCheckpointPayload
        {
            Phase = OrdersImportCheckpointPhase.FetchCompleted,
            TotalBatches = 1,
            MaxChangedAt = FinalisedAt.AddMinutes(-5),
            WindowTo = FinalisedAt,
            PagesFetched = 1,
            IsBackfill = false,
        };
    }
}
