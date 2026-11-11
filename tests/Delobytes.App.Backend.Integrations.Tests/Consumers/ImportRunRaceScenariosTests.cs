using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Jobs;
using Delobytes.App.Backend.Integrations.Application.Orders;
using Delobytes.App.Backend.Integrations.Application.Products;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.Consumers;

/// <summary>
/// Race scenarios every batch-driven import run has to survive, executed against both import
/// paths. Assertions go through each path's entry point only, so the same suite holds before and
/// after the aggregation logic is shared.
/// </summary>
public class ImportRunRaceScenariosTests
{
    public enum ImportPath
    {
        Orders,
        Products,
    }

    [Theory]
    [InlineData(ImportPath.Orders)]
    [InlineData(ImportPath.Products)]
    public async Task TerminalBatchFirst_DoesNotFinaliseBeforeEveryBatchIsRecorded(ImportPath path)
    {
        Scenario scenario = Scenario.Create(path, expectedTotal: 3);

        await scenario.DeliverBatchAsync(announcedTotal: 3);

        scenario.JobRun.Status.Should().Be(JobStatus.Running, "1 of 3 batches is recorded");
        scenario.CompletedEvents.Should().Be(0);

        await scenario.DeliverBatchAsync();

        scenario.JobRun.Status.Should().Be(JobStatus.Running, "2 of 3 batches are recorded");
        scenario.CompletedEvents.Should().Be(0);

        await scenario.DeliverBatchAsync();

        scenario.JobRun.Status.Should().Be(JobStatus.Success);
        scenario.CompletedEvents.Should().Be(1, "the run is finalised exactly once");
        scenario.RecordedBatches.Should().Be(3);
    }

    [Theory]
    [InlineData(ImportPath.Orders)]
    [InlineData(ImportPath.Products)]
    public async Task RedeliveredBatch_IsRecordedOnce(ImportPath path)
    {
        Scenario scenario = Scenario.Create(path, expectedTotal: 3);
        Guid messageId = Guid.NewGuid();

        await scenario.DeliverBatchAsync(messageId: messageId);
        await scenario.DeliverBatchAsync(messageId: messageId);

        scenario.RecordedBatches.Should().Be(1, "MessageId is the idempotency key");
        scenario.CompletedEvents.Should().Be(0);
    }

    [Theory]
    [InlineData(ImportPath.Orders)]
    [InlineData(ImportPath.Products)]
    public async Task LostFinalisationRace_PublishesNothing(ImportPath path)
    {
        Scenario scenario = Scenario.Create(path);
        scenario.LoseConcurrentUpdate();

        Func<Task> act = () => scenario.DeliverBatchAsync(announcedTotal: 1);

        await act.Should().NotThrowAsync("the loser steps aside instead of surfacing the conflict");
        scenario.CompletedEvents.Should().Be(0, "the winner publishes the completion event");
    }

    [Theory]
    [InlineData(ImportPath.Orders)]
    [InlineData(ImportPath.Products)]
    public async Task UnknownBatchCount_DoesNotFinalise(ImportPath path)
    {
        Scenario scenario = Scenario.Create(path, withCheckpoint: false);

        await scenario.DeliverBatchAsync();

        scenario.RecordedBatches.Should().Be(1);
        scenario.JobRun.Status.Should().Be(JobStatus.Running, "nothing has announced how many batches to expect");
        scenario.CompletedEvents.Should().Be(0);
    }

    private sealed class Scenario
    {
        private readonly ImportPath _path;
        private readonly int _expectedTotal;
        private readonly Mock<IJobRunRepository> _jobRunRepository = new();
        private readonly Mock<IJobCheckpointRepository> _jobCheckpointRepository = new();
        private readonly Mock<ISyncCheckpointRepository> _syncCheckpointRepository = new();
        private readonly Mock<IConnectionRepository> _connectionRepository = new();
        private readonly Mock<IEventPublisher> _eventPublisher = new();
        private readonly List<JobRunBatchResult> _storedResults = new();

        private Func<Guid, int?, CancellationToken, Task> _deliver = default!;

        private Scenario(ImportPath path, int expectedTotal)
        {
            _path = path;
            _expectedTotal = expectedTotal;
            JobRun = new JobRun
            {
                Id = Guid.NewGuid(),
                ConnectionId = Guid.NewGuid(),
                JobType = path == ImportPath.Orders ? JobType.WbOrdersImport : JobType.ProductsImport,
                Status = JobStatus.Running,
                CreatedAt = DateTimeOffset.UtcNow,
            };
        }

        public JobRun JobRun { get; }

        public int CompletedEvents { get; private set; }

        public int RecordedBatches => _storedResults.Count;

        public static Scenario Create(ImportPath path, int expectedTotal = 1, bool withCheckpoint = true)
        {
            Scenario scenario = new Scenario(path, expectedTotal);

            scenario.BuildRepository(withCheckpoint);
            scenario.BuildPublisher();
            scenario.BuildPath(withCheckpoint);

            return scenario;
        }

        public Task DeliverBatchAsync(Guid? messageId = null, int? announcedTotal = null)
        {
            return _deliver(messageId ?? Guid.NewGuid(), announcedTotal, CancellationToken.None);
        }

        public void LoseConcurrentUpdate()
        {
            _jobRunRepository
                .Setup(r => r.TryUpdateAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);
        }

        private void BuildRepository(bool withCheckpoint)
        {
            _jobRunRepository
                .Setup(r => r.GetByIdAsync(JobRun.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => JobRun);

            _jobRunRepository
                .Setup(r => r.GetExpectedBatchCountAsync(JobRun.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => JobRun.ExpectedBatchCount);

            _jobRunRepository
                .Setup(r => r.BatchResultExistsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .Returns((Guid messageId, CancellationToken _) =>
                    Task.FromResult(_storedResults.Any(result => result.MessageId == messageId)));

            _jobRunRepository
                .Setup(r => r.AddBatchResult(It.IsAny<JobRunBatchResult>()))
                .Callback<JobRunBatchResult>(result => _storedResults.Add(result));

            _jobRunRepository
                .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(1);

            _jobRunRepository
                .Setup(r => r.TryUpdateAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            _jobRunRepository
                .Setup(r => r.CountBatchResultsAsync(JobRun.Id, It.IsAny<CancellationToken>()))
                .Returns((Guid _, CancellationToken __) => Task.FromResult(_storedResults.Count));

            _jobRunRepository
                .Setup(r => r.GetBatchResultTotalsAsync(JobRun.Id, It.IsAny<CancellationToken>()))
                .Returns((Guid _, CancellationToken __) => Task.FromResult(new JobRunBatchResultTotals
                {
                    TotalProcessed = _storedResults.Sum(result => result.RecordsProcessed),
                    TotalCreated = _storedResults.Sum(result => result.RecordsCreated),
                    TotalUpdated = _storedResults.Sum(result => result.RecordsUpdated),
                    TotalSkipped = _storedResults.Sum(result => result.RecordsSkipped),
                    TotalFailed = _storedResults.Sum(result => result.RecordsFailed),
                    CombinedErrors = null,
                }));

            _jobRunRepository
                .Setup(r => r.GetProductsImportJobAsync(JobRun.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ProductsImportJob { JobRunId = JobRun.Id });

            _jobCheckpointRepository
                .Setup(r => r.GetByJobRunIdAsync(JobRun.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(withCheckpoint ? BuildCheckpoint() : null);

            _syncCheckpointRepository
                .Setup(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<JobType>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((SyncCheckpoint?)null);

            _syncCheckpointRepository
                .Setup(r => r.UpsertAsync(It.IsAny<SyncCheckpoint>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            _connectionRepository
                .Setup(r => r.FindByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Connection?)null);

            _connectionRepository
                .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(1);
        }

        private void BuildPublisher()
        {
            _eventPublisher
                .Setup(p => p.PublishAsync(It.IsAny<OrdersImportCompletedEvent>(), It.IsAny<CancellationToken>()))
                .Callback(() => CompletedEvents++)
                .Returns(Task.CompletedTask);

            _eventPublisher
                .Setup(p => p.PublishAsync(It.IsAny<ProductsImportCompletedEvent>(), It.IsAny<CancellationToken>()))
                .Callback(() => CompletedEvents++)
                .Returns(Task.CompletedTask);

            _eventPublisher
                .Setup(p => p.PublishAsync(
                    It.IsAny<ChannelProductReconciliationRequestedEvent>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        }

        private void BuildPath(bool withCheckpoint)
        {
            BatchImportFinalizer finalizer = new BatchImportFinalizer(
                _jobRunRepository.Object,
                TimeProvider.System,
                NullLogger<BatchImportFinalizer>.Instance);

            ImportRunCompletion completion = new ImportRunCompletion(
                _jobRunRepository.Object,
                finalizer,
                TimeProvider.System,
                NullLogger<ImportRunCompletion>.Instance);

            if (_path == ImportPath.Orders)
            {
                OrdersImportWatermarkWriter watermarkWriter = new OrdersImportWatermarkWriter(
                    _syncCheckpointRepository.Object,
                    _connectionRepository.Object,
                    TimeProvider.System,
                    NullLogger<OrdersImportWatermarkWriter>.Instance);

                OrdersBatchAggregator aggregator = new OrdersBatchAggregator(
                    _jobCheckpointRepository.Object,
                    completion,
                    watermarkWriter,
                    NullLogger<OrdersBatchAggregator>.Instance);

                OrdersImportFinisher finisher = new OrdersImportFinisher(
                    aggregator,
                    _eventPublisher.Object,
                    NullLogger<OrdersImportFinisher>.Instance);

                _deliver = async (messageId, announcedTotal, cancellationToken) =>
                {
                    // The orchestrator writes the checkpoint and publishes the fetch-completed event
                    // before the batch results it describes are consumed, so the announcement is
                    // delivered first here as well.
                    if (announcedTotal.HasValue)
                    {
                        await finisher.ProcessFetchCompletedAsync(
                            new OrdersImportFetchCompletedEvent
                            {
                                JobRunId = JobRun.Id,
                                ConnectionId = JobRun.ConnectionId,
                                TotalBatches = announcedTotal.Value,
                            },
                            cancellationToken);
                    }

                    OrdersBatchImportCompletedEvent batchEvent = new OrdersBatchImportCompletedEvent
                    {
                        JobRunId = JobRun.Id,
                        BatchId = messageId,
                        Created = 5,
                    };

                    await finisher.ProcessAsync(batchEvent, messageId, cancellationToken);
                };

                return;
            }

            ProductsImportBatchAggregator productsAggregator = new ProductsImportBatchAggregator(
                _syncCheckpointRepository.Object,
                _connectionRepository.Object,
                completion,
                _eventPublisher.Object,
                NullLogger<ProductsImportBatchAggregator>.Instance,
                TimeProvider.System);

            _deliver = (messageId, announcedTotal, cancellationToken) =>
                productsAggregator.ProcessAsync(
                    new ProductBatchImportCompletedEvent
                    {
                        JobRunId = JobRun.Id,
                        IsLastBatch = announcedTotal.HasValue,
                        RecordsProcessed = 5,
                        RecordsCreated = 5,
                        TotalBatches = announcedTotal ?? 0,
                    },
                    messageId,
                    cancellationToken);
        }

        private JobCheckpoint BuildCheckpoint()
        {
            return new JobCheckpoint
            {
                JobRunId = JobRun.Id,
                Payload = JsonSerializer.Serialize(new OrdersImportCheckpointPayload
                {
                    Phase = OrdersImportCheckpointPhase.FetchCompleted,
                    TotalBatches = _expectedTotal,
                    MaxChangedAt = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero),
                    WindowTo = DateTimeOffset.UtcNow,
                    PagesFetched = 1,
                    IsBackfill = false,
                }),
                UpdatedAt = DateTimeOffset.UtcNow,
            };
        }
    }
}
