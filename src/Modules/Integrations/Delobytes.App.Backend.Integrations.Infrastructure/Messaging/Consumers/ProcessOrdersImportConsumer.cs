using Delobytes.App.Backend.Integrations.Application.Events;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Application.Options;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using Delobytes.App.Backend.Integrations.Contracts.Models;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
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
    public const int BatchSize = OrdersImportConstants.BatchSize;

    public static readonly TimeSpan SafetyOverlap = OrdersImportConstants.SafetyOverlap;
    public static readonly TimeSpan BackfillWindow = OrdersImportConstants.BackfillWindow;

    private readonly IJobRunRepository _jobRunRepository;
    private readonly OrdersImportCheckpointStore _checkpointStore;
    private readonly OrdersBatchPublisher _batchPublisher;
    private readonly PacedOrdersSource _pacedSource;
    private readonly BisectingOrdersReader _bisectingReader;
    private readonly OrdersBackfillWalker _backfillWalker;
    private readonly ISyncCheckpointRepository _syncCheckpointRepository;
    private readonly IChannelApiClientFactory _clientFactory;
    private readonly IEventPublisher _eventPublisher;
    private readonly JobRunExecutionContext _jobRunExecutionContext;
    private readonly WildberriesImportOptions _options;
    private readonly ILogger<ProcessOrdersImportConsumer> _logger;
    private readonly TimeSpan _minWindow;
    private readonly TimeSpan _backfillRunBudget;

    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _maxInProcessWait;

    public ProcessOrdersImportConsumer(
        IJobRunRepository jobRunRepository,
        IJobCheckpointRepository jobCheckpointRepository,
        ISyncCheckpointRepository syncCheckpointRepository,
        IOrdersBackfillStateRepository ordersBackfillStateRepository,
        IChannelApiClientFactory clientFactory,
        IEventPublisher eventPublisher,
        JobRunExecutionContext jobRunExecutionContext,
        IOptions<WildberriesImportOptions> options,
        ILogger<ProcessOrdersImportConsumer> logger,
        IWildberriesRateLimiter rateLimiter,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory)
    {
        _jobRunRepository = jobRunRepository;
        _checkpointStore = new OrdersImportCheckpointStore(jobCheckpointRepository, timeProvider);
        _batchPublisher = new OrdersBatchPublisher(eventPublisher, _checkpointStore);
        _syncCheckpointRepository = syncCheckpointRepository;
        _clientFactory = clientFactory;
        _eventPublisher = eventPublisher;
        _jobRunExecutionContext = jobRunExecutionContext;
        _options = options.Value;

        _minWindow = _options.MinOrdersWindow > TimeSpan.Zero
            ? _options.MinOrdersWindow
            : TimeSpan.FromHours(1);

        _backfillRunBudget = TimeSpan.FromSeconds(
            _options.BackfillRunBudgetSeconds > 0
                ? _options.BackfillRunBudgetSeconds
                : WildberriesImportOptions.DefaultBackfillRunBudgetSeconds);

        _logger = logger;
        _timeProvider = timeProvider;
        _maxInProcessWait = TimeSpan.FromSeconds(_options.MaxInProcessWaitSeconds >= 0
            ? _options.MaxInProcessWaitSeconds
            : WildberriesImportOptions.DefaultMaxInProcessWaitSeconds);
        if (_backfillRunBudget + _maxInProcessWait >= TimeSpan.FromMinutes(30))
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Run budget plus maximum wait must be below 30 minutes.");
        }

        _pacedSource = new PacedOrdersSource(rateLimiter, timeProvider, _maxInProcessWait);

        // BisectingOrdersReader/OrdersBackfillWalker are internal; a public constructor cannot take
        // ILogger<T> of an internal T (CS0051), so the category-specific loggers are built from the
        // factory here rather than requested as typed constructor parameters.
        _bisectingReader = new BisectingOrdersReader(_pacedSource, new Logger<BisectingOrdersReader>(loggerFactory), _minWindow);
        _backfillWalker = new OrdersBackfillWalker(
            _pacedSource,
            ordersBackfillStateRepository,
            new Logger<OrdersBackfillWalker>(loggerFactory),
            timeProvider,
            BackfillWindow,
            SafetyOverlap);
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
            await FailJobAsync(jobRun, reason);
            return;
        }

        if (jobRun.Status == JobStatus.Pending)
        {
            jobRun.Status = JobStatus.Running;
            jobRun.StartedAt = _timeProvider.GetUtcNow();

            jobRun.NotBefore = null;

            await _jobRunRepository.UpdateAsync(jobRun, cancellationToken);
        }

        _jobRunExecutionContext.SetCurrent(jobRun.Id);

        try
        {
            IChannelApiClient apiClient = _clientFactory.Create(connection.SystemChannelTemplate);

            if (apiClient is not IOrdersSource ordersSource)
            {
                string error = $"Channel '{connection.SystemChannelTemplate.Code}' does not support orders import";
                _logger.LogError("JobRun {JobRunId}: {Error}", jobRun.Id, error);
                await FailJobAsync(jobRun, error);
                return;
            }

            OrdersImportCheckpointPayload? previousPayload = await _checkpointStore
                .LoadAsync(jobRun.Id, cancellationToken);

            if (previousPayload != null && previousPayload.IsFetchCompleted != false)
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

            int requestsIssued = 0;
            int rowsWithoutIdentity = 0;
            int fetchedOrdersCount = 0;
            DateTimeOffset? maxChangedAt = null;
            BackfillOutcome? backfillOutcome = null;

            if (isBackfill)
            {
                backfillOutcome = await _backfillWalker.FetchBackfillDaysAsync(
                    ordersSource,
                    connection,
                    backfillState,
                    jobRun.Id,
                    state,
                    (unit, ct) => _batchPublisher.PublishUnitAsync(jobRun, connection, cursor, unit, ct),
                    cancellationToken);

                requestsIssued = state.RequestsIssued;
                rowsWithoutIdentity = backfillOutcome.RowsWithoutIdentity;
                fetchedOrdersCount = backfillOutcome.TotalOrders;

                if (!backfillState.IsCompleted && backfillOutcome.ResumeAt.HasValue)
                {
                    await CreateContinuationAsync(jobRun, backfillOutcome.ResumeAt.Value, state, cancellationToken);
                }

                _logger.LogInformation(
                    "JobRun {JobRunId}: branch=backfill, days fetched={DaysFetched}, first day={FirstDay}, "
                    + "last day={LastDay}, rows={Rows}, stoppedByThrottle={StoppedByThrottle}, "
                    + "stoppedByBudget={StoppedByBudget}, nextDayToFetch={NextDayToFetch}, isCompleted={IsCompleted}",
                    jobRun.Id,
                    backfillOutcome.DaysFetched,
                    backfillOutcome.FirstDay,
                    backfillOutcome.LastDay,
                    backfillOutcome.TotalOrders,
                    backfillOutcome.StoppedByThrottle,
                    backfillOutcome.StoppedByBudget,
                    backfillState.NextDayToFetch,
                    backfillState.IsCompleted);
            }
            else
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

                OrdersFetchResult fetchResult = await _bisectingReader.FetchAllPagesAsync(
                    ordersSource, connection, request, jobRun.Id, state, cancellationToken);

                requestsIssued = fetchResult.RequestsIssued;
                rowsWithoutIdentity = fetchResult.RowsWithoutIdentity;
                maxChangedAt = OrdersWindowPlanner.GetMaxChangedAt(fetchResult.Orders, windowTo, out DateTimeOffset? observedBeyondWindow);

                if (observedBeyondWindow.HasValue)
                {
                    _logger.LogWarning(
                        "Orders import: observed lastChangeDate {Observed} is later than the run window end {WindowTo}, clamping the watermark",
                        observedBeyondWindow.Value,
                        windowTo);
                }

                await _batchPublisher.PublishUnitAsync(jobRun, connection, cursor, fetchResult.Orders, cancellationToken);

                fetchedOrdersCount = fetchResult.Orders.Count;
            }

            OrdersImportCheckpointPayload payload = new OrdersImportCheckpointPayload
            {
                TotalBatches = cursor.NextIndex + 1,
                MaxChangedAt = maxChangedAt,
                WindowTo = windowTo,
                PagesFetched = requestsIssued,
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
                "JobRun {JobRunId}: backfill={Backfill}, fetched {Orders} orders in {Requests} requests, " +
                "published {Batches} batches, rowsWithoutIdentity={RowsWithoutIdentity}, stopReason={StopReason}, " +
                "inProcessWaits={InProcessWaits}, totalWaitedSeconds={TotalWaitedSeconds}, " +
                "continuationId={ContinuationId}, continuationNotBefore={ContinuationNotBefore}",
                jobRun.Id,
                isBackfill,
                fetchedOrdersCount,
                requestsIssued,
                payload.TotalBatches,
                rowsWithoutIdentity,
                state.StopReason,
                state.InProcessWaits,
                state.TotalWaited.TotalSeconds,
                state.ContinuationId,
                state.ContinuationNotBefore);
        }
        catch (OrdersFetchStoppedException ex)
        {
            _logger.LogWarning(
                "JobRun {JobRunId}: delta stopped, stopReason={StopReason}, inProcessWaits={InProcessWaits}, waitedSeconds={WaitedSeconds}, resumeAt={ResumeAt}",
                jobRun.Id, ex.Reason, state.InProcessWaits, state.TotalWaited.TotalSeconds, ex.ResumeAt);

            await FailJobAsync(jobRun, $"Wildberries rate limit: next request allowed at {ex.ResumeAt:O}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "JobRun {JobRunId}: orders import failed", jobRun.Id);
            await FailJobAsync(jobRun, ex.Message);
        }
        finally
        {
            _jobRunExecutionContext.Clear();
        }
    }

    private async Task CreateContinuationAsync(JobRun current, DateTimeOffset resumeAt, OrdersImportRunState state, CancellationToken cancellationToken)
    {
        bool hasPending = await _jobRunRepository
            .HasPendingJobAsync(current.ConnectionId, JobType.OrdersImport, current.Id, cancellationToken);

        if (hasPending)
        {
            return;
        }

        JobRun continuation = new JobRun
        {
            Id = Guid.NewGuid(),
            ConnectionId = current.ConnectionId,
            JobType = JobType.OrdersImport,
            Status = JobStatus.Pending,
            CreatedAt = _timeProvider.GetUtcNow(),
            NotBefore = resumeAt,
        };

        await _jobRunRepository.CreateAsync(continuation, cancellationToken);

        state.ContinuationId = continuation.Id;
        state.ContinuationNotBefore = resumeAt;
    }

    private async Task FailJobAsync(JobRun jobRun, string error)
    {
        jobRun.Status = JobStatus.Failed;
        jobRun.Error = error;
        jobRun.CompletedAt = _timeProvider.GetUtcNow();

        // The consumer token may already be cancelled; the failure must still be persisted.
        await _jobRunRepository.UpdateAsync(jobRun, CancellationToken.None);
    }
}
