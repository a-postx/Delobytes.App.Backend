using Delobytes.App.Backend.Integrations.Application.Events;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Application.Options;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using Delobytes.App.Backend.Integrations.Contracts.Models;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers.OrdersImport;
using Delobytes.App.Backend.Integrations.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers;

/// <summary>
/// Orchestrates an orders import run: computes the delta/backfill window from SyncCheckpoint,
/// fetches the whole window (narrowing it while the endpoint reports a partial response).
/// </summary>
public class ProcessOrdersImportConsumer
{
    public static readonly TimeSpan SafetyOverlap = OrdersImportConstants.SafetyOverlap;
    public static readonly TimeSpan BackfillWindow = OrdersImportConstants.BackfillWindow;

    private readonly IJobRunRepository _jobRunRepository;
    private readonly OrdersImportCheckpointStore _checkpointStore;
    private readonly OrdersBatchPublisher _batchPublisher;
    private readonly BisectingOrdersReader _bisectingReader;
    private readonly OrdersBackfillWalker _backfillWalker;
    private readonly JobRunLifecycle _jobRunLifecycle;
    private readonly OrdersContinuationScheduler _continuationScheduler;
    private readonly ISyncCheckpointRepository _syncCheckpointRepository;
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
        OrdersBatchPublisher batchPublisher,
        BisectingOrdersReader bisectingReader,
        OrdersBackfillWalker backfillWalker,
        ISyncCheckpointRepository syncCheckpointRepository,
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
        _batchPublisher = batchPublisher;
        _bisectingReader = bisectingReader;
        _backfillWalker = backfillWalker;
        _jobRunLifecycle = jobRunLifecycle;
        _continuationScheduler = continuationScheduler;
        _syncCheckpointRepository = syncCheckpointRepository;
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

    public static Guid CreateBatchId(Guid jobRunId, int batchIndex)
    {
        return OrdersBatchPublisher.CreateBatchId(jobRunId, batchIndex);
    }

    public async Task ProcessAsync(OrdersImportRequestedEvent message, CancellationToken cancellationToken)
    {
        OrdersImportRunState state = new OrdersImportRunState(_timeProvider.GetUtcNow() + _backfillRunBudget);

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

        await _jobRunLifecycle.TryStartAsync(jobRun, cancellationToken);

        using IDisposable scope = _jobRunExecutionContext.EnterScope(jobRun.Id);

        try
        {
            await RunImportAsync(jobRun, connection, state, cancellationToken);
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

    private async Task RunImportAsync(
        JobRun jobRun,
        Connection connection,
        OrdersImportRunState state,
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
            await PublishTerminalForFinishedRunAsync(jobRun, connection, previousPayload, cancellationToken);
            return;
        }

        // A redelivered delta run must re-read the same window, otherwise the batch count
        // persisted for the aggregator would no longer match what is being republished.
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
        // persisted their count, but never reached the finishing write below. Starting over at
        // 0 would reuse BatchIds already recorded for different content under this JobRunId.
        int nextBatchIndex = previousPayload != null && previousPayload.IsFetchCompleted == false
            ? previousPayload.TotalBatches
            : 0;

        OrdersImportBatchCursor cursor = new OrdersImportBatchCursor(nextBatchIndex, windowTo, isBackfill);

        if (previousPayload == null)
        {
            await _checkpointStore.SaveAsync(
                jobRun.Id,
                new OrdersImportCheckpointPayload
                {
                    TotalBatches = 0,
                    WindowTo = windowTo,
                    IsBackfill = isBackfill,
                    IsFetchCompleted = false,
                },
                cancellationToken);
        }

        OrdersWindowFetchResult result = isBackfill
            ? await FetchBackfillAsync(ordersSource, connection, backfillState, jobRun, cursor, state, cancellationToken)
            : await FetchDeltaAsync(ordersSource, connection, backfillState, jobRun, cursor, state, windowTo, cancellationToken);

        if (state.StopReason == StopReason.Throttled || state.StopReason == StopReason.Budget)
        {
            await FinishStoppedAsync(jobRun, connection, state, cursor, windowTo, isBackfill, result, cancellationToken);
            return;
        }

        await FinishCompletedAsync(jobRun, connection, state, cursor, windowTo, isBackfill, backfillState, result, cancellationToken);
    }

    private async Task<OrdersWindowFetchResult> FetchBackfillAsync(
        IOrdersSource ordersSource,
        Connection connection,
        OrdersBackfillState backfillState,
        JobRun jobRun,
        OrdersImportBatchCursor cursor,
        OrdersImportRunState state,
        CancellationToken cancellationToken)
    {
        BackfillOutcome outcome = await _backfillWalker.FetchBackfillDaysAsync(
            ordersSource,
            jobRun,
            connection,
            cursor,
            backfillState,
            state,
            cancellationToken);

        _logger.LogInformation(
            "JobRun {JobRunId}: branch=backfill, days fetched={DaysFetched}, first day={FirstDay}, "
            + "last day={LastDay}, rows={Rows}, stopReason={StopReason}, "
            + "nextDayToFetch={NextDayToFetch}, isCompleted={IsCompleted}",
            jobRun.Id,
            outcome.DaysFetched,
            outcome.FirstDay,
            outcome.LastDay,
            outcome.TotalOrders,
            state.StopReason,
            backfillState.NextDayToFetch,
            backfillState.IsCompleted);

        return new OrdersWindowFetchResult
        {
            Orders = new List<ChannelOrderSnapshot>(),
            RequestsIssued = state.RequestsIssued,
            RowsWithoutIdentity = 0,
            StopReason = state.StopReason,
            ResumeAt = state.ResumeAt,
            MaxChangedAt = null,
        };
    }

    private async Task<OrdersWindowFetchResult> FetchDeltaAsync(
        IOrdersSource ordersSource,
        Connection connection,
        OrdersBackfillState backfillState,
        JobRun jobRun,
        OrdersImportBatchCursor cursor,
        OrdersImportRunState state,
        DateTimeOffset windowTo,
        CancellationToken cancellationToken)
    {
        SyncCheckpoint? syncCheckpoint = await _syncCheckpointRepository
            .GetAsync(connection.Id, JobType.OrdersImport, cancellationToken);

        DateTimeOffset windowFrom = OrdersWindowPlanner.ResolveDeltaWindowFrom(
            syncCheckpoint, backfillState, out bool usedLegacyFallback);

        if (usedLegacyFallback)
        {
            _logger.LogWarning(
                "JobRun {JobRunId}: legacy completed backfill has no handover watermark; using walk start {WindowFrom}",
                jobRun.Id, windowFrom);
        }

        OrdersRequest request = new OrdersRequest
        {
            DateFrom = windowFrom.ToOffset(OrdersWindowPlanner.MoscowOffset),
            DateTo = windowTo.ToOffset(OrdersWindowPlanner.MoscowOffset),
            Flag = 0,
            Limit = 0,
        };

        _logger.LogInformation(
            "JobRun {JobRunId}: branch=delta, fetching orders from {From} to {To}",
            jobRun.Id,
            request.DateFrom,
            request.DateTo);

        OrdersWindowFetchResult fetchResult = await _bisectingReader.FetchAllPagesAsync(
            ordersSource, connection, request, jobRun.Id, state, cancellationToken);

        if (fetchResult.IsStopped)
        {
            // Nothing already read is published: it belongs to an unread window, so publishing it
            // would advance the watermark over orders the bisection never saw.
            state.StopReason = fetchResult.StopReason;
            state.ResumeAt = fetchResult.ResumeAt;

            return fetchResult;
        }

        DateTimeOffset? maxChangedAt = OrdersWindowPlanner.GetMaxChangedAt(
            fetchResult.Orders, windowTo, out DateTimeOffset? observedBeyondWindow);

        if (observedBeyondWindow.HasValue)
        {
            _logger.LogWarning(
                "Orders import: observed lastChangeDate {Observed} is later than the run window end {WindowTo}, clamping the watermark",
                observedBeyondWindow.Value,
                windowTo);
        }

        await _batchPublisher.PublishUnitAsync(jobRun, connection, cursor, fetchResult.Orders, cancellationToken);

        return fetchResult with
        {
            MaxChangedAt = maxChangedAt,
        };
    }

    /// <summary>
    /// A run stopped by throttle or budget leaves its data incomplete, so it neither finalises the
    /// job (no IsFetchCompleted=true, no terminal batch) nor reports a failure: it persists the
    /// progress checkpoint and schedules a continuation. Both branches share this outcome so the
    /// operator sees the same thing in JobRun history regardless of which one stopped.
    /// </summary>
    private async Task FinishStoppedAsync(
        JobRun jobRun,
        Connection connection,
        OrdersImportRunState state,
        OrdersImportBatchCursor cursor,
        DateTimeOffset windowTo,
        bool isBackfill,
        OrdersWindowFetchResult result,
        CancellationToken cancellationToken)
    {
        await _checkpointStore.SaveAsync(
            jobRun.Id,
            new OrdersImportCheckpointPayload
            {
                TotalBatches = cursor.NextIndex,
                WindowTo = windowTo,
                PagesFetched = result.RequestsIssued,
                IsBackfill = isBackfill,
                IsFetchCompleted = false,
            },
            cancellationToken);

        if (state.ResumeAt.HasValue)
        {
            JobRun? continuation = await _continuationScheduler
                .ScheduleAsync(jobRun, state.ResumeAt.Value, cancellationToken);

            if (continuation != null)
            {
                state.ContinuationId = continuation.Id;
                state.ContinuationNotBefore = continuation.NotBefore;
            }
        }

        _logger.LogWarning(
            "JobRun {JobRunId}: stopped, backfill={Backfill}, stopReason={StopReason}, "
            + "fetched {Orders} orders, resumeAt={ResumeAt}, inProcessWaits={InProcessWaits}, "
            + "totalWaitedSeconds={TotalWaitedSeconds}, continuationId={ContinuationId}, "
            + "continuationNotBefore={ContinuationNotBefore}",
            jobRun.Id,
            isBackfill,
            state.StopReason,
            result.Orders.Count,
            state.ResumeAt,
            state.InProcessWaits,
            state.TotalWaited.TotalSeconds,
            state.ContinuationId,
            state.ContinuationNotBefore);
    }

    private async Task FinishCompletedAsync(
        JobRun jobRun,
        Connection connection,
        OrdersImportRunState state,
        OrdersImportBatchCursor cursor,
        DateTimeOffset windowTo,
        bool isBackfill,
        OrdersBackfillState backfillState,
        OrdersWindowFetchResult result,
        CancellationToken cancellationToken)
    {
        OrdersImportCheckpointPayload payload = new OrdersImportCheckpointPayload
        {
            TotalBatches = cursor.NextIndex + 1,
            MaxChangedAt = result.MaxChangedAt,
            WindowTo = windowTo,
            PagesFetched = result.RequestsIssued,
            IsBackfill = isBackfill,
            IsBackfillCompleted = isBackfill && backfillState.IsCompleted,
            HandoverWatermark = isBackfill ? backfillState.HandoverWatermark : null,
            IsFetchCompleted = true,
        };

        // The final count must be persisted before the terminal batch is published: the
        // aggregator finalises on the terminal result (or on any later data result that brings
        // the count up to TotalBatches), and it can only do that once it knows the final count.
        await _checkpointStore.SaveAsync(jobRun.Id, payload, cancellationToken);

        await _batchPublisher.PublishTerminalAsync(jobRun, connection, cursor.NextIndex, cancellationToken);

        _logger.LogInformation(
            "JobRun {JobRunId}: completed, backfill={Backfill}, rows={Rows}, requests={Requests}, "
            + "published {Batches} batches, rowsWithoutIdentity={RowsWithoutIdentity}, stopReason={StopReason}, "
            + "inProcessWaits={InProcessWaits}, totalWaitedSeconds={TotalWaitedSeconds}, "
            + "continuationId={ContinuationId}, continuationNotBefore={ContinuationNotBefore}",
            jobRun.Id,
            isBackfill,
            result.Orders.Count,
            result.RequestsIssued,
            payload.TotalBatches,
            result.RowsWithoutIdentity,
            state.StopReason,
            state.InProcessWaits,
            state.TotalWaited.TotalSeconds,
            state.ContinuationId,
            state.ContinuationNotBefore);
    }

    private async Task PublishTerminalForFinishedRunAsync(
        JobRun jobRun,
        Connection connection,
        OrdersImportCheckpointPayload previousPayload,
        CancellationToken cancellationToken)
    {
        await _eventPublisher.PublishAsync(new OrdersBatchImportRequestedEvent
        {
            JobRunId = jobRun.Id,
            ConnectionId = connection.Id,
            ChannelId = connection.ChannelId,
            BatchId = CreateBatchId(jobRun.Id, previousPayload.TotalBatches - 1),
            Orders = new List<ChannelOrderSnapshot>(),
            IsTerminal = true,
        }, cancellationToken);
    }
}
