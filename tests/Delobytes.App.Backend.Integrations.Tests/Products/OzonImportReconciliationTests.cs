using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Jobs;
using Delobytes.App.Backend.Integrations.Application.Products;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.Products;

/// <summary>
/// The full-set sweep deactivates every link whose LastSyncedAt predates the run. Ozon imports only
/// VISIBLE cards, so a temporarily hidden card would be deactivated by it — a channel-side change
/// must not change anything on our side. The sweep is therefore gated on the job type.
/// </summary>
public class OzonImportReconciliationTests
{
    private readonly Mock<IJobRunRepository> _jobRunRepo = new();
    private readonly Mock<ISyncCheckpointRepository> _syncCheckpointRepo = new();
    private readonly Mock<IConnectionRepository> _connectionRepo = new();
    private readonly Mock<IEventPublisher> _eventPublisher = new();
    private readonly Mock<ILogger<ProductsImportBatchAggregator>> _logger = new();

    [Fact]
    public async Task ProcessAsync_OzonProductsRun_DoesNotRequestFullSetReconciliation()
    {
        List<ChannelProductReconciliationRequestedEvent> reconciliationEvents =
            await RunFinalBatchAsync(JobType.OzonProductsImport);

        reconciliationEvents.Should().BeEmpty(
            "an Ozon import reads only visible cards, so a sweep would deactivate hidden ones");
    }

    [Fact]
    public async Task ProcessAsync_WildberriesProductsRun_StillRequestsFullSetReconciliation()
    {
        List<ChannelProductReconciliationRequestedEvent> reconciliationEvents =
            await RunFinalBatchAsync(JobType.ProductsImport);

        reconciliationEvents.Should().ContainSingle();
    }

    private async Task<List<ChannelProductReconciliationRequestedEvent>> RunFinalBatchAsync(JobType jobType)
    {
        JobRun jobRun = new JobRun
        {
            Id = Guid.NewGuid(),
            ConnectionId = Guid.NewGuid(),
            JobType = jobType,
            Status = JobStatus.Running,
            CreatedAt = DateTimeOffset.UtcNow,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
        };

        Guid messageId = Guid.NewGuid();

        _jobRunRepo
            .Setup(r => r.GetByIdAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(jobRun);
        _jobRunRepo
            .Setup(r => r.GetExpectedBatchCountAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => jobRun.ExpectedBatchCount);
        _jobRunRepo
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _jobRunRepo
            .Setup(r => r.TryUpdateAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _jobRunRepo
            .Setup(r => r.BatchResultExistsAsync(messageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _jobRunRepo
            .Setup(r => r.GetBatchResultTotalsAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobRunBatchResultTotals
            {
                TotalProcessed = 10,
                TotalCreated = 10,
                TotalUpdated = 0,
                TotalSkipped = 0,
                TotalFailed = 0,
                CombinedErrors = null,
            });
        _jobRunRepo
            .Setup(r => r.CountBatchResultsAsync(jobRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _syncCheckpointRepo
            .Setup(r => r.GetAsync(jobRun.ConnectionId, jobRun.JobType, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncCheckpoint?)null);

        Connection connection = new Connection
        {
            Id = jobRun.ConnectionId,
            ChannelId = Guid.NewGuid(),
            Name = "Test channel",
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

        _eventPublisher
            .Setup(p => p.PublishAsync(It.IsAny<ProductsImportCompletedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        List<ChannelProductReconciliationRequestedEvent> captured =
            new List<ChannelProductReconciliationRequestedEvent>();

        _eventPublisher
            .Setup(p => p.PublishAsync(
                It.IsAny<ChannelProductReconciliationRequestedEvent>(),
                It.IsAny<CancellationToken>()))
            .Callback<ChannelProductReconciliationRequestedEvent, CancellationToken>((e, _) => captured.Add(e))
            .Returns(Task.CompletedTask);

        BatchImportFinalizer finalizer = new BatchImportFinalizer(
            _jobRunRepo.Object,
            TimeProvider.System,
            NullLogger<BatchImportFinalizer>.Instance);

        ImportRunCompletion completion = new ImportRunCompletion(
            _jobRunRepo.Object,
            finalizer,
            TimeProvider.System,
            NullLogger<ImportRunCompletion>.Instance);

        ProductsImportBatchAggregator aggregator = new ProductsImportBatchAggregator(
            _syncCheckpointRepo.Object,
            _connectionRepo.Object,
            completion,
            _eventPublisher.Object,
            _logger.Object,
            TimeProvider.System);

        await aggregator.ProcessAsync(
            new ProductBatchImportCompletedEvent
            {
                JobRunId = jobRun.Id,
                IsLastBatch = true,
                RecordsProcessed = 10,
                RecordsCreated = 10,
                RecordsUpdated = 0,
                RecordsSkipped = 0,
                RecordsFailed = 0,
                TotalBatches = 1,
            },
            messageId,
            CancellationToken.None);

        return captured;
    }
}
