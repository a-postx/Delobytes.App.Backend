using Delobytes.App.Backend.Integrations.Application.Events;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Options;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers.OrdersImport;
using Delobytes.App.Backend.Integrations.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers;

/// <summary>
/// Orchestrates an orders import run: picks the fetcher for the run, awaits it once, and turns its
/// outcome into a checkpoint, a continuation or a failure. Which fetch algorithm runs, and what a
/// stop means for it, belongs to the fetcher.
/// </summary>
public class ProcessOrdersImportConsumer
{
    private readonly IJobRunRepository _jobRunRepository;
    private readonly OrdersImportCheckpointStore _checkpointStore;
    private readonly OrdersBackfillWalker _backfillWalker;
    private readonly IOrdersFetcher _backfillFetcher;
    private readonly IOrdersFetcher _deltaFetcher;
    private readonly JobRunLifecycle _jobRunLifecycle;
    private readonly OrdersContinuationScheduler _continuationScheduler;
    private readonly IChannelApiClientFactory _clientFactory;
    private readonly IEventPublisher _eventPublisher;
    private readonly JobRunExecutionContext _jobRunExecutionContext;
    private readonly ILogger<ProcessOrdersImportConsumer> _logger;
    private readonly TimeSpan _backfillRunBudget;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _maxInProcessWait;

    public ProcessOrdersImportConsumer(
        IJobRunRepository jobRunRepository,
        OrdersImportCheckpointStore checkpointStore,
        OrdersBackfillWalker backfillWalker,
        BackfillOrdersFetcher backfillFetcher,
        DeltaOrdersFetcher deltaFetcher,
        IChannelApiClientFactory clientFactory,
        IEventPublisher eventPublisher,
        JobRunExecutionContext jobRunExecutionContext,
        IOptions<WildberriesImportOptions> options,
        ILogger<ProcessOrdersImportConsumer> logger,
        TimeProvider timeProvider,
        JobRunLifecycle jobRunLifecycle,
        OrdersContinuationScheduler continuationScheduler)
    {
        WildberriesImportOptions importOptions = options.Value;

        _jobRunRepository = jobRunRepository;
        _checkpointStore = checkpointStore;
        _backfillWalker = backfillWalker;
        _backfillFetcher = backfillFetcher;
        _deltaFetcher = deltaFetcher;
        _jobRunLifecycle = jobRunLifecycle;
        _continuationScheduler = continuationScheduler;
        _clientFactory = clientFactory;
        _eventPublisher = eventPublisher;
        _jobRunExecutionContext = jobRunExecutionContext;
        _logger = logger;
        _timeProvider = timeProvider;

        _backfillRunBudget = TimeSpan.FromSeconds(
            importOptions.BackfillRunBudgetSeconds > 0
                ? importOptions.BackfillRunBudgetSeconds
                : WildberriesImportOptions.DefaultBackfillRunBudgetSeconds);

        _maxInProcessWait = TimeSpan.FromSeconds(importOptions.MaxInProcessWaitSeconds >= 0
            ? importOptions.MaxInProcessWaitSeconds
            : WildberriesImportOptions.DefaultMaxInProcessWaitSeconds);

        if (_backfillRunBudget + _maxInProcessWait >= TimeSpan.FromMinutes(30))
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Run budget plus maximum wait must be below 30 minutes.");
        }
    }

    public async Task ProcessAsync(OrdersImportRequestedEvent message, CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = _timeProvider.GetUtcNow() + _backfillRunBudget;

        _logger.LogInformation(
            "ProcessOrdersImportConsumer: JobRunId={JobRunId}, ConnectionId={ConnectionId}",
            message.JobRunId,
            message.ConnectionId);

        JobRun? jobRun = await _jobRunRepository.GetByIdWithConnectionAsync(message.JobRunId, cancellationToken);

        if (jobRun == null)
        {
            _logger.LogWarning("JobRun {JobRunId} not found, skipping", message.JobRunId);
            return;
        }

        if (jobRun.Status != JobStatus.Pending && jobRun.Status != JobStatus.Running)
        {
            _logger.LogWarning(
                "JobRun {JobRunId} is in terminal status {Status}, skipping",
                jobRun.Id,
                jobRun.Status);
            return;
        }

        Connection? connection = jobRun.Connection;

        if (connection == null || !connection.IsActive)
        {
            string reason = connection == null ? "Connection not found" : "Connection is inactive";
            _logger.LogError("JobRun {JobRunId}: {Reason}. Marking as Failed.", jobRun.Id, reason);
            await _jobRunLifecycle.FailAsync(jobRun, reason, cancellationToken);
            return;
        }

        await _jobRunLifecycle.StartAsync(jobRun, cancellationToken);

        using (IDisposable scope = _jobRunExecutionContext.EnterScope(jobRun.Id))
        {
            try
            {
                await RunImportAsync(jobRun, connection, deadline, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "JobRun {JobRunId}: orders import failed", jobRun.Id);
                await _jobRunLifecycle.FailAsync(jobRun, ex.Message, cancellationToken);
            }
        }
    }

    private async Task RunImportAsync(
        JobRun jobRun,
        Connection connection,
        DateTimeOffset deadline,
        CancellationToken cancellationToken)
    {
        IChannelApiClient apiClient = _clientFactory.Create(connection.SystemChannelTemplate);

        if (apiClient is not IOrdersSource ordersSource)
        {
            string error = $"Channel '{connection.SystemChannelTemplate.Code}' does not support orders import";
            _logger.LogError("JobRun {JobRunId}: {Error}", jobRun.Id, error);
            await _jobRunLifecycle.FailAsync(jobRun, error, cancellationToken);
            return;
        }

        OrdersImportCheckpointPayload? previousPayload = await _checkpointStore
            .LoadAsync(jobRun.Id, cancellationToken);

        if (previousPayload != null && previousPayload.IsFetchCompleted != false)
        {
            await PublishFetchCompletedForFinishedRunAsync(jobRun, connection, previousPayload, cancellationToken);
            return;
        }

        OrdersImportRun run = await CreateRunAsync(
            jobRun, connection, deadline, ordersSource, previousPayload, cancellationToken);

        IOrdersFetcher fetcher = run.IsBackfill ? _backfillFetcher : _deltaFetcher;

        FetchOutcome outcome = await fetcher.FetchAsync(run, cancellationToken);

        if (outcome.IsStopped)
        {
            await FinishStoppedAsync(run, outcome, cancellationToken);
            return;
        }

        await FinishCompletedAsync(run, outcome, cancellationToken);
    }

    private async Task<OrdersImportRun> CreateRunAsync(
        JobRun jobRun,
        Connection connection,
        DateTimeOffset deadline,
        IOrdersSource ordersSource,
        OrdersImportCheckpointPayload? previousPayload,
        CancellationToken cancellationToken)
    {
        // A redelivered run must re-read the same window, otherwise the batch count persisted for
        // the aggregator would no longer match what is being republished.
        DateTimeOffset windowTo = previousPayload?.WindowTo ?? _timeProvider.GetUtcNow();

        OrdersBackfillState backfillState = await _backfillWalker.GetOrCreateAsync(connection.Id, cancellationToken);

        // The walk decides the branch, not the watermark. A connection that was syncing under
        // the old single-request backfill has an incomplete history precisely because that
        // request imported one calendar day out of ninety, so it earns the walk too. Re-reading
        // stored days is idempotent on the (ChannelId, ExternalOrderId) upsert; leaving the hole
        // in place is not recoverable.
        bool isBackfill = previousPayload?.IsBackfill == true || !backfillState.IsCompleted;

        // A redelivered message continues batch numbering where the last attempt left off,
        // identified by IsFetchCompleted == false: that attempt published some units and
        // persisted their count, but never reached the finishing write. Starting over at 0 would
        // reuse BatchIds already recorded for different content under this JobRunId.
        int nextBatchIndex = previousPayload != null && previousPayload.IsFetchCompleted == false
            ? previousPayload.TotalBatches
            : 0;

        OrdersImportRun run = new OrdersImportRun(jobRun, connection, deadline, windowTo, isBackfill, nextBatchIndex)
        {
            OrdersSource = ordersSource,
            BackfillState = backfillState,
        };

        if (previousPayload == null)
        {
            await _checkpointStore.SaveAsync(
                jobRun.Id,
                new OrdersImportCheckpointPayload
                {
                    TotalBatches = 0,
                    WindowTo = run.WindowTo,
                    IsBackfill = run.IsBackfill,
                    IsFetchCompleted = false,
                },
                cancellationToken);
        }

        return run;
    }

    /// <summary>
    /// A run stopped by throttle or budget leaves its data incomplete, so it neither finalises the
    /// job (no IsFetchCompleted=true, no completion event) nor reports a failure: it persists the
    /// progress checkpoint and schedules a continuation.
    /// </summary>
    private async Task FinishStoppedAsync(
        OrdersImportRun run,
        FetchOutcome outcome,
        CancellationToken cancellationToken)
    {
        await _checkpointStore.SaveAsync(
            run.JobRun.Id,
            new OrdersImportCheckpointPayload
            {
                TotalBatches = run.NextBatchIndex,
                WindowTo = run.WindowTo,
                PagesFetched = outcome.RequestsIssued,
                IsBackfill = run.IsBackfill,
                IsFetchCompleted = false,
            },
            cancellationToken);

        Guid? continuationId = null;
        DateTimeOffset? continuationNotBefore = null;

        if (outcome.ResumeAt.HasValue)
        {
            JobRun? continuation = await _continuationScheduler
                .ScheduleAsync(run.JobRun, outcome.ResumeAt.Value, cancellationToken);

            if (continuation != null)
            {
                continuationId = continuation.Id;
                continuationNotBefore = continuation.NotBefore;
            }
        }

        _logger.LogWarning(
            "JobRun {JobRunId}: stopped, backfill={Backfill}, stopReason={StopReason}, "
            + "fetched {Orders} orders, resumeAt={ResumeAt}, inProcessWaits={InProcessWaits}, "
            + "totalWaitedSeconds={TotalWaitedSeconds}, continuationId={ContinuationId}, "
            + "continuationNotBefore={ContinuationNotBefore}",
            run.JobRun.Id,
            run.IsBackfill,
            outcome.StopReason,
            outcome.OrdersFetched,
            outcome.ResumeAt,
            run.InProcessWaits,
            run.TotalWaited.TotalSeconds,
            continuationId,
            continuationNotBefore);
    }

    private async Task FinishCompletedAsync(
        OrdersImportRun run,
        FetchOutcome outcome,
        CancellationToken cancellationToken)
    {
        OrdersImportCheckpointPayload payload = new OrdersImportCheckpointPayload
        {
            TotalBatches = run.NextBatchIndex,
            MaxChangedAt = outcome.MaxChangedAt,
            WindowTo = run.WindowTo,
            PagesFetched = outcome.RequestsIssued,
            IsBackfill = run.IsBackfill,
            IsBackfillCompleted = run.IsBackfill && outcome.IsBackfillCompleted,
            HandoverWatermark = run.IsBackfill ? outcome.HandoverWatermark : null,
            IsFetchCompleted = true,
        };

        // The final count must be persisted before the completion event is published: the
        // aggregator finalises on whichever of the completion event or a batch result brings its
        // recorded count up to the expected total, and it can only do that once it knows the
        // final count.
        await _checkpointStore.SaveAsync(run.JobRun.Id, payload, cancellationToken);

        await _eventPublisher.PublishAsync(
            BuildFetchCompletedEvent(run.JobRun.Id, run.Connection.Id, payload),
            cancellationToken);

        _logger.LogInformation(
            "JobRun {JobRunId}: completed, backfill={Backfill}, rows={Rows}, requests={Requests}, "
            + "published {Batches} batches, inProcessWaits={InProcessWaits}, totalWaitedSeconds={TotalWaitedSeconds}",
            run.JobRun.Id,
            run.IsBackfill,
            outcome.OrdersFetched,
            outcome.RequestsIssued,
            payload.TotalBatches,
            run.InProcessWaits,
            run.TotalWaited.TotalSeconds);
    }

    private async Task PublishFetchCompletedForFinishedRunAsync(
        JobRun jobRun,
        Connection connection,
        OrdersImportCheckpointPayload previousPayload,
        CancellationToken cancellationToken)
    {
        await _eventPublisher.PublishAsync(
            BuildFetchCompletedEvent(jobRun.Id, connection.Id, previousPayload),
            cancellationToken);
    }

    private static OrdersImportFetchCompletedEvent BuildFetchCompletedEvent(
        Guid jobRunId,
        Guid connectionId,
        OrdersImportCheckpointPayload payload)
    {
        return new OrdersImportFetchCompletedEvent
        {
            JobRunId = jobRunId,
            ConnectionId = connectionId,
            TotalBatches = payload.TotalBatches,
            MaxChangedAt = payload.MaxChangedAt,
            HandoverWatermark = payload.HandoverWatermark,
            IsBackfill = payload.IsBackfill,
            IsBackfillCompleted = payload.IsBackfillCompleted,
            PagesFetched = payload.PagesFetched,
            WindowTo = payload.WindowTo,
        };
    }
}
