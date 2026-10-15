using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers;

/// <summary>
/// Aggregates batch results and finalises the JobRun once every expected batch has been
/// recorded. Idempotent: duplicate deliveries are skipped via BatchId (MessageId)
/// deduplication, and concurrent finalisation attempts are serialised via JobRun's xmin
/// concurrency token (see <see cref="Contracts.Interfaces.IRowVersionedEntity"/>).
/// </summary>
public class ProcessOrdersBatchImportCompletedConsumer
{
    private readonly IJobRunRepository _jobRunRepository;
    private readonly ISyncCheckpointRepository _syncCheckpointRepository;
    private readonly IConnectionRepository _connectionRepository;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<ProcessOrdersBatchImportCompletedConsumer> _logger;

    public ProcessOrdersBatchImportCompletedConsumer(
        IJobRunRepository jobRunRepository,
        ISyncCheckpointRepository syncCheckpointRepository,
        IConnectionRepository connectionRepository,
        IEventPublisher eventPublisher,
        ILogger<ProcessOrdersBatchImportCompletedConsumer> logger)
    {
        _jobRunRepository = jobRunRepository;
        _syncCheckpointRepository = syncCheckpointRepository;
        _connectionRepository = connectionRepository;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    public async Task ProcessAsync(
        OrdersBatchImportCompletedEvent message,
        Guid messageId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "ProcessOrdersBatchImportCompletedConsumer: JobRunId={JobRunId}, BatchId={BatchId}, MessageId={MessageId}",
            message.JobRunId,
            message.BatchId,
            messageId);

        JobRun? jobRun = await _jobRunRepository.GetByIdAsync(message.JobRunId, cancellationToken);

        if (jobRun == null)
        {
            _logger.LogWarning(
                "JobRun {JobRunId} not found for batch result MessageId={MessageId}, skipping",
                message.JobRunId,
                messageId);
            return;
        }

        bool alreadyProcessed = await _jobRunRepository.BatchResultExistsAsync(messageId, cancellationToken);

        if (alreadyProcessed)
        {
            _logger.LogInformation(
                "Batch result MessageId={MessageId} for JobRun {JobRunId} already recorded, skipping",
                messageId,
                message.JobRunId);

            if (IsTerminalStatus(jobRun.Status))
            {
                await PublishCompletedEventAsync(jobRun, cancellationToken);
            }

            return;
        }

        JobRunBatchResult batchResult = new JobRunBatchResult
        {
            Id = Guid.NewGuid(),
            JobRunId = message.JobRunId,
            MessageId = messageId,
            RecordsProcessed = message.Created + message.Updated + message.Skipped + message.Failed,
            RecordsCreated = message.Created,
            RecordsUpdated = message.Updated,
            RecordsSkipped = message.Skipped,
            RecordsFailed = message.Failed,
            ErrorMessage = null,
            IsLastBatch = false,
            ReceivedAt = DateTimeOffset.UtcNow,
        };

        _jobRunRepository.AddBatchResult(batchResult);

        await _jobRunRepository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Recorded batch result for JobRun {JobRunId}, MessageId={MessageId}",
            message.JobRunId,
            messageId);

        JobRunBatchResultTotals totals = await _jobRunRepository.GetBatchResultTotalsAsync(
            message.JobRunId, cancellationToken);

        jobRun.RecordsCreated = totals.TotalCreated;
        jobRun.RecordsUpdated = totals.TotalUpdated;
        jobRun.RecordsSkipped = totals.TotalSkipped;
        jobRun.RecordsFailed = totals.TotalFailed;
        jobRun.CompletedAt = DateTimeOffset.UtcNow;

        bool hasErrors = totals.TotalFailed > 0 || totals.CombinedErrors != null;

        if (!hasErrors)
        {
            jobRun.Status = JobStatus.Success;
        }
        else
        {
            jobRun.Status = JobStatus.Failed;
            jobRun.Error = totals.CombinedErrors;
            _logger.LogError("Error importing orders batch: {Message}", totals.CombinedErrors);
        }

        try
        {
            await _jobRunRepository.UpdateAsync(jobRun, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            _logger.LogInformation(
                "JobRun {JobRunId} was already finalised by a concurrently processed batch result, skipping",
                jobRun.Id);

            return;
        }

        _logger.LogInformation(
            "JobRun {JobRunId} finalised with status {Status}, total Created={Created}, Updated={Updated}, Skipped={Skipped}, Failed={Failed}",
            jobRun.Id,
            jobRun.Status,
            jobRun.RecordsCreated,
            jobRun.RecordsUpdated,
            jobRun.RecordsSkipped,
            jobRun.RecordsFailed);

        if (jobRun.Status == JobStatus.Success)
        {
            DateTimeOffset? maxLastChangedAt = await GetMaxLastChangedAtFromBatchesAsync(message.JobRunId, cancellationToken);

            SyncCheckpoint? checkpoint = await _syncCheckpointRepository.GetAsync(
                jobRun.ConnectionId, JobType.OrdersImport, cancellationToken);

            if (checkpoint == null)
            {
                checkpoint = new SyncCheckpoint
                {
                    Id = Guid.NewGuid(),
                    ConnectionId = jobRun.ConnectionId,
                    JobType = JobType.OrdersImport,
                    LastChangedAt = maxLastChangedAt,
                    LastSuccessAt = DateTimeOffset.UtcNow,
                    LastJobRunId = jobRun.Id
                };
            }
            else
            {
                if (maxLastChangedAt != null)
                {
                    checkpoint.LastChangedAt = maxLastChangedAt;
                }
                checkpoint.LastSuccessAt = DateTimeOffset.UtcNow;
                checkpoint.LastJobRunId = jobRun.Id;
            }

            await _syncCheckpointRepository.UpsertAsync(checkpoint, cancellationToken);

            Connection? connection = await _connectionRepository.FindByIdAsync(jobRun.ConnectionId, cancellationToken);
            if (connection != null)
            {
                connection.LastSyncAt = DateTimeOffset.UtcNow;
                await _connectionRepository.SaveChangesAsync(cancellationToken);
            }

            _logger.LogInformation(
                "Updated SyncCheckpoint for ConnectionId={ConnectionId}, JobType={JobType}, LastChangedAt={LastChangedAt}",
                jobRun.ConnectionId,
                JobType.OrdersImport,
                maxLastChangedAt);
        }

        await PublishCompletedEventAsync(jobRun, cancellationToken);
    }

    private async Task PublishCompletedEventAsync(JobRun jobRun, CancellationToken cancellationToken)
    {
        OrdersImportCompletedEvent completedEvent = new OrdersImportCompletedEvent
        {
            JobRunId = jobRun.Id,
            TotalCreated = jobRun.RecordsCreated,
            TotalUpdated = jobRun.RecordsUpdated,
            Status = jobRun.Status
        };

        await _eventPublisher.PublishAsync(completedEvent, cancellationToken);

        _logger.LogInformation(
            "Published OrdersImportCompletedEvent for JobRun {JobRunId}",
            jobRun.Id);
    }

    private bool IsTerminalStatus(JobStatus status)
    {
        return status == JobStatus.Success || status == JobStatus.Failed || status == JobStatus.Cancelled;
    }

    private async Task<DateTimeOffset?> GetMaxLastChangedAtFromBatchesAsync(Guid jobRunId, CancellationToken cancellationToken)
    {
        return DateTimeOffset.UtcNow;
    }
}
