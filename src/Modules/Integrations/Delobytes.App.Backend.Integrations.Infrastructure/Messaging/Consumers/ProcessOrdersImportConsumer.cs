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
    public const int BatchSize = 30;

    /// <summary>
    /// Hard bound on the number of HTTP requests one run may issue. Bisection of a window does
    /// not loop, but the bound protects against a pathological configuration.
    /// </summary>
    public const int MaxRequestsPerRun = 1000;

    public static readonly TimeSpan SafetyOverlap = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan BackfillWindow = TimeSpan.FromDays(180);

    private static readonly TimeSpan MoscowOffset = TimeSpan.FromHours(3);

    private readonly IJobRunRepository _jobRunRepository;
    private readonly OrdersImportCheckpointStore _checkpointStore;
    private readonly OrdersBatchPublisher _batchPublisher;
    private readonly ISyncCheckpointRepository _syncCheckpointRepository;
    private readonly IOrdersBackfillStateRepository _ordersBackfillStateRepository;
    private readonly IChannelApiClientFactory _clientFactory;
    private readonly IEventPublisher _eventPublisher;
    private readonly JobRunExecutionContext _jobRunExecutionContext;
    private readonly WildberriesImportOptions _options;
    private readonly ILogger<ProcessOrdersImportConsumer> _logger;
    private readonly TimeSpan _minWindow;
    private readonly TimeSpan _backfillRunBudget;

    private readonly TimeProvider _timeProvider;
    private readonly IWildberriesRateLimiter _rateLimiter;
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
        TimeProvider timeProvider)
    {
        _jobRunRepository = jobRunRepository;
        _checkpointStore = new OrdersImportCheckpointStore(jobCheckpointRepository, timeProvider);
        _batchPublisher = new OrdersBatchPublisher(eventPublisher, _checkpointStore);
        _syncCheckpointRepository = syncCheckpointRepository;
        _ordersBackfillStateRepository = ordersBackfillStateRepository;
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
        _rateLimiter = rateLimiter;
        _timeProvider = timeProvider;
        _maxInProcessWait = TimeSpan.FromSeconds(_options.MaxInProcessWaitSeconds >= 0
            ? _options.MaxInProcessWaitSeconds
            : WildberriesImportOptions.DefaultMaxInProcessWaitSeconds);
        if (_backfillRunBudget + _maxInProcessWait >= TimeSpan.FromMinutes(30))
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Run budget plus maximum wait must be below 30 minutes.");
        }
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

            OrdersBackfillState backfillState = await GetOrCreateBackfillStateAsync(connection.Id, cancellationToken);

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
                backfillOutcome = await FetchBackfillDaysAsync(
                    ordersSource,
                    connection,
                    backfillState,
                    jobRun.Id,
                    (unit, ct) => _batchPublisher.PublishUnitAsync(jobRun, connection, cursor, unit, ct),
                    state,
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

                DateTimeOffset windowFrom;

                if (syncCheckpoint?.LastChangedAt != null)
                {
                    windowFrom = syncCheckpoint.LastChangedAt.Value - SafetyOverlap;
                }
                else
                {
                    windowFrom = backfillState.HandoverWatermark
                        ?? backfillState.OldestDayInScope + BackfillWindow;

                    if (!backfillState.HandoverWatermark.HasValue)
                    {
                        _logger.LogWarning(
                            "JobRun {JobRunId}: legacy completed backfill has no handover watermark; using walk start {WindowFrom}",
                            jobRun.Id, windowFrom);
                    }
                }

                OrdersRequest request = new OrdersRequest
                {
                    DateFrom = windowFrom.ToOffset(MoscowOffset),
                    DateTo = windowTo.ToOffset(MoscowOffset),
                    Flag = 0,
                    Limit = 0,
                };

                _logger.LogInformation(
                    "JobRun {JobRunId}: branch=delta, fetching orders from {From} to {To}",
                    jobRun.Id,
                    request.DateFrom,
                    request.DateTo);

                OrdersFetchResult fetchResult = await FetchAllPagesAsync(
                    ordersSource, connection, request, jobRun.Id, state, cancellationToken);

                requestsIssued = fetchResult.RequestsIssued;
                rowsWithoutIdentity = fetchResult.RowsWithoutIdentity;
                maxChangedAt = GetMaxChangedAt(fetchResult.Orders, windowTo);

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

    /// <summary>
    /// Deterministic per (JobRunId, index) so a redelivered orchestrator republishes the same
    /// BatchIds and the aggregator deduplicates them instead of double counting.
    /// </summary>
    public static Guid CreateBatchId(Guid jobRunId, int batchIndex)
    {
        return OrdersBatchPublisher.CreateBatchId(jobRunId, batchIndex);
    }

    /// <summary>
    /// Loads the per-connection backfill cursor, creating it on first contact. A fresh cursor starts
    /// at today 00:00 MSK and walks backwards to today - <see cref="BackfillWindow"/>, which is the
    /// scope the single-request backfill used to cover.
    /// </summary>
    private async Task<OrdersBackfillState> GetOrCreateBackfillStateAsync(Guid connectionId, CancellationToken cancellationToken)
    {
        OrdersBackfillState? state = await _ordersBackfillStateRepository.GetAsync(connectionId, cancellationToken);

        if (state != null)
        {
            return state;
        }

        DateTimeOffset todayStart = GetMoscowDayStart(_timeProvider.GetUtcNow());

        state = new OrdersBackfillState
        {
            Id = Guid.NewGuid(),
            ConnectionId = connectionId,
            NextDayToFetch = todayStart,
            OldestDayInScope = todayStart - BackfillWindow,
            IsCompleted = false,
        };

        await _ordersBackfillStateRepository.UpsertAsync(state, cancellationToken);

        return state;
    }

    private async Task<BackfillOutcome> FetchBackfillDaysAsync(
        IOrdersSource ordersSource,
        Connection connection,
        OrdersBackfillState state,
        Guid jobRunId,
        Func<List<ChannelOrderSnapshot>, CancellationToken, Task> publishUnit,
        OrdersImportRunState runState,
        CancellationToken cancellationToken)
    {
        int daysFetched = 0;
        int totalOrders = 0;
        DateTimeOffset? firstDay = null;
        DateTimeOffset? lastDay = null;
        DateTimeOffset? resumeAt = null;

        while (!state.IsCompleted)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DateTimeOffset now = _timeProvider.GetUtcNow();
            DateTimeOffset retentionStart = GetMoscowDayStart(now - BackfillWindow);
            DateTimeOffset oldestDay = state.OldestDayInScope > retentionStart
                ? state.OldestDayInScope : retentionStart;

            if (!state.CatchUpNextDay.HasValue && state.NextDayToFetch < oldestDay)
            {
                int skippedDays = Math.Max(0, (int)(state.NextDayToFetch - state.OldestDayInScope).TotalDays + 1);
                if (skippedDays > 0)
                {
                    _logger.LogWarning("JobRun {JobRunId}: skipped {SkippedDays} backfill day(s) outside retention", jobRunId, skippedDays);
                }

                state.CatchUpNextDay = GetMoscowDayStart(state.OldestDayInScope + BackfillWindow);
                await _ordersBackfillStateRepository.UpsertAsync(state, cancellationToken);
            }

            bool isCatchUp = state.CatchUpNextDay.HasValue;
            DateTimeOffset day = GetMoscowDayStart(isCatchUp ? state.CatchUpNextDay.GetValueOrDefault() : state.NextDayToFetch);

            OrdersRequest request = new OrdersRequest
            {
                DateFrom = day,
                DateTo = day.AddDays(1).AddSeconds(-1),
                Flag = 1,
                Limit = 0,
            };

            OrdersPage page;

            try
            {
                page = await GetPacedOrdersAsync(ordersSource, connection, request, runState, cancellationToken);
            }
            catch (OrdersFetchStoppedException ex)
            {
                runState.StopReason = ex.Reason;
                resumeAt = ex.ResumeAt;
                break;
            }

            if (page.RowsWithoutIdentity > 0)
            {
                throw new InvalidOperationException($"Orders backfill received {page.RowsWithoutIdentity} row(s) without a usable order identity for day {day:yyyy-MM-dd} MSK.");
            }

            if (page.HasMore)
            {
                throw new InvalidOperationException($"Orders backfill received a partial response for day {day:yyyy-MM-dd} MSK; flag=1 cannot be split below one day.");
            }

            List<ChannelOrderSnapshot> dayOrders = OrderSnapshotMerger.MergeByExternalOrderId(page.Orders);

            await publishUnit(
                dayOrders.OrderBy(order => order.ExternalOrderId, StringComparer.Ordinal).ToList(),
                cancellationToken);

            totalOrders += dayOrders.Count;
            daysFetched++;
            firstDay ??= day;
            lastDay = day;

            if (isCatchUp)
            {
                if (day >= GetMoscowDayStart(_timeProvider.GetUtcNow()))
                {
                    state.HandoverWatermark = runState.LastRequestStartedAt - SafetyOverlap;
                    state.IsCompleted = true;
                    state.CompletedAt = _timeProvider.GetUtcNow();
                    state.CatchUpNextDay = day;
                }
                else
                {
                    state.CatchUpNextDay = day.AddDays(1);
                }
            }
            else
            {
                state.NextDayToFetch = day.AddDays(-1);
            }

            await _ordersBackfillStateRepository.UpsertAsync(state, cancellationToken);
        }

        return new BackfillOutcome
        {
            TotalOrders = totalOrders,
            DaysFetched = daysFetched,
            RowsWithoutIdentity = 0,
            StoppedByThrottle = runState.StopReason == "Throttled",
            StoppedByBudget = runState.StopReason == "Budget",
            FirstDay = firstDay,
            LastDay = lastDay,
            ResumeAt = resumeAt,
        };
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

    private async Task<OrdersPage> GetPacedOrdersAsync(
        IOrdersSource ordersSource,
        Connection connection,
        OrdersRequest request,
        OrdersImportRunState state,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            DateTimeOffset now = _timeProvider.GetUtcNow();

            if (now >= state.Deadline)
            {
                DateTimeOffset? nextAllowedAt = await _rateLimiter.GetNextRequestAllowedAtAsync(
                    connection.ApiKey, WildberriesApiMethod.SupplierOrders, cancellationToken);

                DateTimeOffset budgetTime;

                if (nextAllowedAt is not null)
                {
                    budgetTime = nextAllowedAt.Value > now ? nextAllowedAt.Value : now;
                }
                else
                {
                    budgetTime = now;
                }

                throw new OrdersFetchStoppedException("Budget", budgetTime);
            }

            if (state.RequestsIssued >= MaxRequestsPerRun)
            {
                throw new InvalidOperationException($"Orders fetch exceeded the per-run request budget of {MaxRequestsPerRun}.");
            }

            state.LastRequestStartedAt = now;
            state.RequestsIssued++;

            OrdersPage page = await ordersSource.GetOrdersAsync(connection.Id, request, cancellationToken);

            if (!page.Throttled)
            {
                return page;
            }

            DateTimeOffset retryAt = page.RetryAt
                ?? throw new InvalidOperationException("Throttled orders page has no retry time.");
            now = _timeProvider.GetUtcNow();
            TimeSpan wait = retryAt > now ? retryAt - now : TimeSpan.Zero;

            if (wait > _maxInProcessWait || retryAt >= state.Deadline)
            {
                throw new OrdersFetchStoppedException("Throttled", retryAt);
            }

            state.InProcessWaits++;
            DateTimeOffset waitStartedAt = _timeProvider.GetUtcNow();

            await Task.Delay(wait, _timeProvider, cancellationToken);

            state.TotalWaited += _timeProvider.GetUtcNow() - waitStartedAt;
        }
    }

    private sealed class OrdersFetchStoppedException : Exception
    {
        public OrdersFetchStoppedException(string reason, DateTimeOffset resumeAt)
        {
            Reason = reason;
            ResumeAt = resumeAt;
        }

        public string Reason { get; }

        public DateTimeOffset ResumeAt { get; }
    }

    private static DateTimeOffset GetMoscowDayStart(DateTimeOffset instant)
    {
        DateTimeOffset moscow = instant.ToOffset(MoscowOffset);

        return new DateTimeOffset(
            moscow.Year,
            moscow.Month,
            moscow.Day,
            0,
            0,
            0,
            MoscowOffset);
    }

    private async Task<OrdersFetchResult> FetchAllPagesAsync(
        IOrdersSource ordersSource,
        Connection connection,
        OrdersRequest request,
        Guid jobRunId,
        OrdersImportRunState state,
        CancellationToken cancellationToken)
    {
        Dictionary<string, ChannelOrderSnapshot> ordersByOrderKey = new Dictionary<string, ChannelOrderSnapshot>(StringComparer.Ordinal);

        state.RowsWithoutIdentity = 0;

        await FetchWindowAsync(ordersSource, connection, request, jobRunId, ordersByOrderKey, state, cancellationToken);

        return new OrdersFetchResult
        {
            Orders = ordersByOrderKey.Values.ToList(),
            RequestsIssued = state.RequestsIssued,
            RowsWithoutIdentity = state.RowsWithoutIdentity,
        };
    }

    /// <summary>
    /// Fetches one window. The endpoint serves rows in ascending order of the selection instant and
    /// caps a response at a fixed row count, so a capped response proves that everything up to its
    /// last returned instant was served and leaves only the region above that instant unknown. The
    /// recursion therefore splits that unknown region at its midpoint and resumes the left half from
    /// the covered boundary, not from the requested <c>dateFrom</c>: the endpoint has no
    /// <c>dateTo</c>, so re-requesting the parent's own <c>dateFrom</c> would return the identical
    /// block, report the cap again, and never advance. Rows falling exactly on a boundary are
    /// reported by both siblings; the dedup dictionary collapses them into one order and unions
    /// their lines, so the bisection can cut through a cart without losing or duplicating a unit.
    /// </summary>
    private async Task FetchWindowAsync(
        IOrdersSource ordersSource,
        Connection connection,
        OrdersRequest request,
        Guid jobRunId,
        Dictionary<string, ChannelOrderSnapshot> ordersByOrderKey,
        OrdersImportRunState state,
        CancellationToken cancellationToken)
    {
        OrdersPage ordersPage = await GetPacedOrdersAsync(ordersSource, connection, request, state, cancellationToken);

        if (ordersPage.RowsWithoutIdentity > 0)
        {
            // A page that dropped rows cannot be imported partially: the missing orders would be
            // indistinguishable from orders that never existed, and advancing the watermark past
            // this window would hide them from every later delta run as well. Failing here reuses
            // the same fail-fast stance as the unresolvable partial response below, and lets the
            // next scheduled run retry the window once the source data is complete.
            state.RowsWithoutIdentity += ordersPage.RowsWithoutIdentity;

            _logger.LogError(
                "JobRun {JobRunId}: window {From} - {To} returned {RowsWithoutIdentity} row(s) without a usable order identity, failing the run",
                jobRunId,
                request.DateFrom,
                request.DateTo,
                ordersPage.RowsWithoutIdentity);

            throw new InvalidOperationException(
                $"Orders import received {ordersPage.RowsWithoutIdentity} row(s) without a usable order identity " +
                $"(no gNumber, or no srid and no sticker) in window {request.DateFrom:O} - {request.DateTo:O}; " +
                "the window cannot be imported without inventing an identity that may split or merge real orders.");
        }

        foreach (ChannelOrderSnapshot order in ordersPage.Orders)
        {
            // Sibling windows of one bisection share their boundary instant, so an order placed on
            // that instant is legitimately reported by both halves. That is one order observed
            // twice, not two orders colliding, so the reports are merged — never re-keyed.
            if (!ordersByOrderKey.TryGetValue(order.ExternalOrderId, out ChannelOrderSnapshot? existing))
            {
                ordersByOrderKey.Add(order.ExternalOrderId, order);
                continue;
            }

            ordersByOrderKey[order.ExternalOrderId] = OrderSnapshotMerger.Merge(existing, order);
        }

        // The endpoint filters by dateFrom alone and serves rows in ascending order of the selection
        // instant, capped at MaxOrdersPerResponse rows. A capped response therefore proves that
        // everything up to its last returned instant was served, and says nothing about what lies
        // beyond it. Bisecting the requested window instead re-issues the parent's own request: the
        // left half keeps the parent's dateFrom, the endpoint has no dateTo, so it returns the same
        // block, reports the cap again, and recurses down to the window floor without having read a
        // single new row - failing the run on volume alone. The bisection has to start where the
        // unknown region starts, which is the covered boundary.
        // A null CoveredUntil on a capped response means the source could not prove how far it
        // reached; falling back to the requested DateFrom keeps the run honest, since it narrows
        // and retries rather than assuming the window was served.
        DateTimeOffset nextFrom = request.DateFrom;

        if (ordersPage.CoveredUntil != null && ordersPage.CoveredUntil.Value > nextFrom)
        {
            nextFrom = ordersPage.CoveredUntil.Value;
        }

        // Nothing is left unknown: either the response came back whole, or it reached the window end
        // before hitting the cap, in which case the rows past the window are what filled it.
        if (!ordersPage.HasMore || nextFrom >= request.DateTo)
        {
            return;
        }

        TimeSpan unknownSpan = request.DateTo - nextFrom;

        if (unknownSpan <= _minWindow)
        {
            // Accepting the truncated response would silently drop orders so the operator sees
            // a window granularity that the endpoint cannot serve rather than a partial import
            // reported as success.
            _logger.LogWarning(
                "JobRun {JobRunId}: window {From} - {To} still returns a partial response with {Remaining} left to read at the minimum granularity of {MinWindow}, failing the run",
                jobRunId,
                nextFrom,
                request.DateTo,
                unknownSpan,
                _minWindow);

            throw new InvalidOperationException(
                $"Orders import cannot narrow window {nextFrom:O} - {request.DateTo:O} below {_minWindow} " +
                "while the endpoint keeps reporting a partial response.");
        }

        DateTimeOffset midpoint = nextFrom + TimeSpan.FromTicks(unknownSpan.Ticks / 2);

        OrdersRequest firstHalf = new OrdersRequest
        {
            DateFrom = nextFrom,
            DateTo = midpoint,
            Flag = request.Flag,
            Limit = request.Limit,
        };

        OrdersRequest secondHalf = new OrdersRequest
        {
            DateFrom = midpoint,
            DateTo = request.DateTo,
            Flag = request.Flag,
            Limit = request.Limit,
        };

        _logger.LogInformation(
            "JobRun {JobRunId}: window {From} - {To} exceeded the response cap, covered until {CoveredUntil}, splitting at {Midpoint}",
            jobRunId,
            request.DateFrom,
            request.DateTo,
            nextFrom,
            midpoint);

        await FetchWindowAsync(ordersSource, connection, firstHalf, jobRunId, ordersByOrderKey, state, cancellationToken);
        await FetchWindowAsync(ordersSource, connection, secondHalf, jobRunId, ordersByOrderKey, state, cancellationToken);
    }

    /// <summary>
    /// Derives the next delta watermark from what the endpoint actually selected on:
    /// <c>lastChangeDate</c> for a flag=0 run.
    /// </summary>
    private DateTimeOffset? GetMaxChangedAt(List<ChannelOrderSnapshot> orders, DateTimeOffset windowTo)
    {
        DateTimeOffset? max = null;

        foreach (ChannelOrderSnapshot order in orders)
        {
            if (order.StatusChangedAt == null)
            {
                continue;
            }

            DateTimeOffset value = order.StatusChangedAt.Value;

            if (max == null || value > max.Value)
            {
                max = value;
            }
        }

        if (max.HasValue && max.Value > windowTo)
        {
            _logger.LogWarning(
                "Orders import: observed lastChangeDate {Observed} is later than the run window end {WindowTo}, clamping the watermark",
                max.Value,
                windowTo);

            return windowTo;
        }

        return max?.ToUniversalTime();
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
