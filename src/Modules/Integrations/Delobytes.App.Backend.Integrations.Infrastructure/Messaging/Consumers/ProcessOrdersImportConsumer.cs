using System.Security.Cryptography;
using System.Text.Json;
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
/// fetches the whole window (narrowing it while the endpoint reports a partial response), persists
/// the expected batch count in JobCheckpoint and publishes OrdersBatchImportRequestedEvent per
/// batch. Not user-cancellable (scheduled job).
/// </summary>
public class ProcessOrdersImportConsumer
{
    public const int BatchSize = 30;

    /// <summary>
    /// Hard bound on the number of HTTP requests one run may issue. Bisection of a window does
    /// not loop, but the bound protects against a pathological configuration (a floor far below
    /// the requested window) turning one run into an unbounded request storm.
    /// </summary>
    public const int MaxRequestsPerRun = 1000;

    public static readonly TimeSpan SafetyOverlap = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan BackfillWindow = TimeSpan.FromDays(90);

    private static readonly TimeSpan MoscowOffset = TimeSpan.FromHours(3);

    private readonly IJobRunRepository _jobRunRepository;
    private readonly IJobCheckpointRepository _jobCheckpointRepository;
    private readonly ISyncCheckpointRepository _syncCheckpointRepository;
    private readonly IChannelApiClientFactory _clientFactory;
    private readonly IEventPublisher _eventPublisher;
    private readonly JobRunExecutionContext _jobRunExecutionContext;
    private readonly WildberriesImportOptions _options;
    private readonly ILogger<ProcessOrdersImportConsumer> _logger;
    private readonly TimeSpan _minWindow;
    private readonly TimeSpan _backfillRunBudget;

    /// <summary>
    /// Repository for the per-connection backfill cursor.
    /// </summary>
    private readonly IOrdersBackfillStateRepository _ordersBackfillStateRepository;

    private int _requestsIssued;

    /// <summary>
    /// Rows the source reported as unmappable across every page of the current fetch. Reset per
    /// fetch; non-zero always ends the run in failure, so it exists to make the count visible in
    /// logs and checkpoints rather than to influence the outcome.
    /// </summary>
    private int _rowsWithoutIdentity;

    public ProcessOrdersImportConsumer(
        IJobRunRepository jobRunRepository,
        IJobCheckpointRepository jobCheckpointRepository,
        ISyncCheckpointRepository syncCheckpointRepository,
        IOrdersBackfillStateRepository ordersBackfillStateRepository,
        IChannelApiClientFactory clientFactory,
        IEventPublisher eventPublisher,
        JobRunExecutionContext jobRunExecutionContext,
        IOptions<WildberriesImportOptions> options,
        ILogger<ProcessOrdersImportConsumer> logger)
    {
        _jobRunRepository = jobRunRepository;
        _jobCheckpointRepository = jobCheckpointRepository;
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
    }

    public async Task ProcessAsync(OrdersImportRequestedEvent message, CancellationToken cancellationToken)
    {
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

        OrdersImportJob? ordersImportJob = await _jobRunRepository.GetOrdersImportJobAsync(jobRun.Id, cancellationToken);

        if (ordersImportJob == null)
        {
            _logger.LogWarning("JobRun {JobRunId}: OrdersImportJob row is missing, continuing", jobRun.Id);
        }

        if (jobRun.Status == JobStatus.Pending)
        {
            jobRun.Status = JobStatus.Running;
            jobRun.StartedAt = DateTimeOffset.UtcNow;
            await _jobRunRepository.UpdateAsync(jobRun, cancellationToken);
        }

        // Correlates every HTTP call made through apiClient below with this JobRun, so
        // RawApiResponseCaptureHandler can attribute captured rows to it. The finally below covers
        // every exit path of the try, including the early returns inside it.
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

            JobCheckpoint? existingJobCheckpoint = await _jobCheckpointRepository
                .GetByJobRunIdAsync(jobRun.Id, cancellationToken);
            OrdersImportCheckpointPayload? previousPayload = DeserializePayload(existingJobCheckpoint?.Payload);

            // A redelivered delta run must re-read the same window, otherwise the batch count
            // persisted for the aggregator would no longer match what is being republished.
            DateTimeOffset windowTo = previousPayload?.WindowTo ?? DateTimeOffset.UtcNow;

            OrdersBackfillState backfillState = await GetOrCreateBackfillStateAsync(connection.Id, cancellationToken);

            // The walk decides the branch, not the watermark. A connection that was syncing under
            // the old single-request backfill has an incomplete history precisely because that
            // request imported one calendar day out of ninety, so it earns the walk too. Re-reading
            // stored days is idempotent on the (ChannelId, ExternalOrderId) upsert; leaving the hole
            // in place is not recoverable.
            bool isBackfill = !backfillState.IsCompleted;

            // A redelivered message continues batch numbering where the last attempt left off,
            // identified by IsFetchCompleted == false: that attempt published some units and
            // persisted their count, but never reached the finishing write below. Starting over at
            // 0 would reuse BatchIds already recorded for different content under this JobRunId.
            int nextBatchIndex = previousPayload != null && previousPayload.IsFetchCompleted == false
                ? previousPayload.TotalBatches
                : 0;

            // Publishes one unit of work (one backfill day, or the whole delta window) as soon as
            // it is fetched, then persists how many batches have been published so far so a crash
            // immediately afterwards cannot lose more than the next unit. An empty unit publishes
            // nothing: there is no batch to count and no progress to persist.
            async Task PublishUnitAsync(List<ChannelOrderSnapshot> unitOrders, CancellationToken publishCancellationToken)
            {
                if (unitOrders.Count == 0)
                {
                    return;
                }

                foreach (List<ChannelOrderSnapshot> batch in SplitIntoBatches(unitOrders))
                {
                    OrdersBatchImportRequestedEvent batchEvent = new OrdersBatchImportRequestedEvent
                    {
                        JobRunId = jobRun.Id,
                        ConnectionId = connection.Id,
                        ChannelId = connection.ChannelId,
                        BatchId = CreateBatchId(jobRun.Id, nextBatchIndex),
                        Orders = batch,
                        IsTerminal = false,
                    };

                    await _eventPublisher.PublishAsync(batchEvent, publishCancellationToken);
                    nextBatchIndex++;
                }

                OrdersImportCheckpointPayload progressPayload = new OrdersImportCheckpointPayload
                {
                    TotalBatches = nextBatchIndex,
                    WindowTo = windowTo,
                    IsBackfill = isBackfill,
                    IsFetchCompleted = false,
                };

                await _jobCheckpointRepository.UpsertAsync(
                    new JobCheckpoint
                    {
                        JobRunId = jobRun.Id,
                        Payload = JsonSerializer.Serialize(progressPayload),
                        UpdatedAt = DateTimeOffset.UtcNow,
                    },
                    publishCancellationToken);
            }

            int requestsIssued = 0;
            int rowsWithoutIdentity = 0;
            int fetchedOrdersCount = 0;
            DateTimeOffset? maxChangedAt = null;
            BackfillOutcome? backfillOutcome = null;

            if (isBackfill)
            {
                backfillOutcome = await FetchBackfillDaysAsync(
                    ordersSource, connection.Id, backfillState, jobRun.Id, PublishUnitAsync, cancellationToken);

                requestsIssued = backfillOutcome.DaysFetched;
                rowsWithoutIdentity = backfillOutcome.RowsWithoutIdentity;
                fetchedOrdersCount = backfillOutcome.TotalOrders;

                // A backwards walk earns no watermark. The newest day is fetched first, so
                // publishing its instant as the delta watermark would mark the older days still
                // to come as already imported. The aggregator seeds the watermark from the running
                // maximum instead, and only on the run that finishes the walk.
                maxChangedAt = null;

                _logger.LogInformation(
                    "JobRun {JobRunId}: branch=backfill, days fetched={DaysFetched}, first day={FirstDay}, last day={LastDay}, rows={Rows}, stoppedByThrottle={StoppedByThrottle}, stoppedByBudget={StoppedByBudget}, nextDayToFetch={NextDayToFetch}, isCompleted={IsCompleted}",
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
                    // The walk is complete but the aggregator has not seeded the watermark yet: it
                    // writes it when the last batch result of the completing run arrives, which can
                    // lag this run by one schedule tick. A multi-day flag=1 request must not be used
                    // here - it selects one calendar day out of the whole span and would import
                    // almost nothing - so the delta selection is applied over the backfilled scope
                    // instead. flag=0 is unbounded above, which re-reads the scope in full and is
                    // idempotent on the (ChannelId, ExternalOrderId) upsert.
                    windowFrom = backfillState.MaxObservedLastChangeDate ?? backfillState.OldestDayInScope;

                    _logger.LogWarning(
                        "JobRun {JobRunId}: backfill is complete but SyncCheckpoint.LastChangedAt is still empty, falling back to the backfilled scope start {WindowFrom}",
                        jobRun.Id,
                        windowFrom);
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
                    ordersSource, connection.Id, request, jobRun.Id, cancellationToken);

                requestsIssued = fetchResult.RequestsIssued;
                rowsWithoutIdentity = fetchResult.RowsWithoutIdentity;
                maxChangedAt = GetMaxChangedAt(fetchResult.Orders, windowTo);

                await PublishUnitAsync(fetchResult.Orders, cancellationToken);
                fetchedOrdersCount = fetchResult.Orders.Count;
            }

            OrdersImportCheckpointPayload payload = new OrdersImportCheckpointPayload
            {
                TotalBatches = nextBatchIndex + 1,
                MaxChangedAt = maxChangedAt,
                WindowTo = windowTo,
                PagesFetched = requestsIssued,
                IsBackfill = isBackfill,
                IsBackfillCompleted = isBackfill && backfillState.IsCompleted,
                MaxObservedLastChangeDate = isBackfill ? backfillState.MaxObservedLastChangeDate : null,
                IsFetchCompleted = true,
            };

            // The final count must be persisted before the terminal batch is published: the
            // aggregator finalises on the terminal result (or on any later data result that brings
            // the count up to TotalBatches), and it can only do that once it knows the final count.
            await _jobCheckpointRepository.UpsertAsync(
                new JobCheckpoint
                {
                    JobRunId = jobRun.Id,
                    Payload = JsonSerializer.Serialize(payload),
                    UpdatedAt = DateTimeOffset.UtcNow,
                },
                cancellationToken);

            OrdersBatchImportRequestedEvent terminalBatchEvent = new OrdersBatchImportRequestedEvent
            {
                JobRunId = jobRun.Id,
                ConnectionId = connection.Id,
                ChannelId = connection.ChannelId,
                BatchId = CreateBatchId(jobRun.Id, nextBatchIndex),
                Orders = new List<ChannelOrderSnapshot>(),
                IsTerminal = true,
            };

            await _eventPublisher.PublishAsync(terminalBatchEvent, cancellationToken);

            if (backfillOutcome != null && backfillOutcome.IsStateChanged)
            {
                // The cursor moves only now, after every batch of this run is published. A crash
                // before this point re-fetches these days on the next run rather than losing them;
                // order persistence upserts by (ChannelId, ExternalOrderId), so the repeat is
                // idempotent. Spending scarce request slots on a re-fetch is recoverable, a cursor
                // that ran ahead of the published batches is a silent hole in the history.
                await _ordersBackfillStateRepository.UpsertAsync(backfillState, cancellationToken);
            }

            _logger.LogInformation(
                "JobRun {JobRunId}: branch={Branch}, fetched {Orders} orders in {Requests} requests, published {Batches} batches, rowsWithoutIdentity={RowsWithoutIdentity}",
                jobRun.Id,
                isBackfill ? "backfill" : "delta",
                fetchedOrdersCount,
                requestsIssued,
                payload.TotalBatches,
                rowsWithoutIdentity);
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
        byte[] input = new byte[20];
        jobRunId.ToByteArray().CopyTo(input, 0);
        BitConverter.GetBytes(batchIndex).CopyTo(input, 16);
        byte[] hash = MD5.HashData(input);
        return new Guid(hash);
    }

    /// <summary>
    /// Loads the per-connection backfill cursor, creating it on first contact. A fresh cursor starts
    /// at today 00:00 MSK and walks backwards to today - <see cref="BackfillWindow"/>, which is the
    /// scope the single-request backfill used to cover. Starting today rather than 90 days ago is
    /// what puts recent orders in the UI after the first run.
    /// </summary>
    private async Task<OrdersBackfillState> GetOrCreateBackfillStateAsync(
        Guid connectionId,
        CancellationToken cancellationToken)
    {
        OrdersBackfillState? state = await _ordersBackfillStateRepository.GetAsync(connectionId, cancellationToken);

        if (state != null)
        {
            return state;
        }

        DateTimeOffset todayStart = GetMoscowDayStart(DateTimeOffset.UtcNow);

        return new OrdersBackfillState
        {
            Id = Guid.NewGuid(),
            ConnectionId = connectionId,
            NextDayToFetch = todayStart,
            OldestDayInScope = todayStart - BackfillWindow,
            IsCompleted = false,
        };
    }

    /// <summary>
    /// Walks the backfill days that fit in this run's time budget, newest first, one <c>flag=1</c>
    /// request per calendar day. Each fetched day is published as one unit of work as soon as it is
    /// fetched. The cursor on <paramref name="state"/> is advanced in memory only: the caller
    /// persists it after the batches of this run are published.
    /// </summary>
    private async Task<BackfillOutcome> FetchBackfillDaysAsync(
        IOrdersSource ordersSource,
        Guid connectionId,
        OrdersBackfillState state,
        Guid jobRunId,
        Func<List<ChannelOrderSnapshot>, CancellationToken, Task> publishUnit,
        CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + _backfillRunBudget;
        DateTimeOffset dayCursor = GetMoscowDayStart(state.NextDayToFetch);
        DateTimeOffset oldestDay = GetMoscowDayStart(state.OldestDayInScope);

        int daysFetched = 0;
        int totalOrders = 0;
        int rowsWithoutIdentity = 0;
        bool stoppedByThrottle = false;
        bool stoppedByBudget = false;
        DateTimeOffset? firstDay = null;
        DateTimeOffset? lastDay = null;
        DateTimeOffset? maxObservedLastChangeDate = state.MaxObservedLastChangeDate;

        while (dayCursor >= oldestDay)
        {
            if (DateTimeOffset.UtcNow >= deadline)
            {
                stoppedByBudget = true;

                _logger.LogInformation(
                    "JobRun {JobRunId}: backfill budget of {BudgetSeconds}s exhausted after {DaysFetched} day(s), stopping before {NextDay}, the next run resumes there",
                    jobRunId,
                    _backfillRunBudget.TotalSeconds,
                    daysFetched,
                    dayCursor);

                break;
            }

            // flag=1 selects orders whose order date falls on one calendar day, so the window must
            // not span a midnight: 00:00:00 to 23:59:59 MSK of the same date keeps both bounds on
            // that date, and the client rejects any flag=1 request whose bounds straddle two.
            OrdersRequest request = new OrdersRequest
            {
                DateFrom = dayCursor,
                DateTo = dayCursor.AddDays(1).AddSeconds(-1),
                Flag = 1,
                Limit = 0,
            };

            OrdersPage ordersPage = await ordersSource.GetOrdersAsync(connectionId, request, cancellationToken);

            if (ordersPage.Throttled)
            {
                // Nothing was fetched for this day, so the cursor must not move past it: the run
                // ends here and the next scheduled run retries the same day.
                stoppedByThrottle = true;

                break;
            }

            if (ordersPage.RowsWithoutIdentity > 0)
            {
                rowsWithoutIdentity += ordersPage.RowsWithoutIdentity;

                _logger.LogError(
                    "JobRun {JobRunId}: backfill day {Day} returned {RowsWithoutIdentity} row(s) without a usable order identity, failing the run",
                    jobRunId,
                    dayCursor,
                    ordersPage.RowsWithoutIdentity);

                throw new InvalidOperationException(
                    $"Orders backfill received {ordersPage.RowsWithoutIdentity} row(s) without a usable order identity " +
                    $"(no gNumber, or no srid and no sticker) for day {dayCursor:yyyy-MM-dd} MSK; " +
                    "the day cannot be imported without inventing an identity that may split or merge real orders.");
            }

            if (ordersPage.HasMore)
            {
                // A day is the smallest window this endpoint can be asked for: flag=1 has no
                // sub-day granularity and the endpoint has no dateTo, so a partial day cannot be
                // narrowed and re-requested. Importing what came back would silently drop orders,
                // which is the defect this work removes.
                _logger.LogError(
                    "JobRun {JobRunId}: backfill day {Day} came back truncated at the response cap",
                    jobRunId,
                    dayCursor);

                throw new InvalidOperationException(
                    $"Orders backfill received a partial response for day {dayCursor:yyyy-MM-dd} MSK: " +
                    $"the day holds more rows than the {_options.MaxOrdersPerResponse}-row response cap " +
                    "and flag=1 cannot be split below one day. The day cannot be imported completely.");
            }

            Dictionary<string, ChannelOrderSnapshot> dayByOrderKey = new Dictionary<string, ChannelOrderSnapshot>(StringComparer.Ordinal);

            foreach (ChannelOrderSnapshot order in ordersPage.Orders)
            {
                if (dayByOrderKey.TryGetValue(order.ExternalOrderId, out ChannelOrderSnapshot? existing))
                {
                    // One calendar day is disjoint from every other in this run, so a repeat
                    // identity inside one day is the source reporting the same order twice rather
                    // than two halves of a split cart. Keeping the later report matches the delta
                    // path's merge rule; the lines of the earlier report are preserved when the
                    // later one does not carry them.
                    List<ChannelOrderLineSnapshot> mergedLines = UnionLines(existing.Lines, order.Lines);
                    ChannelOrderSnapshot headerSource = IsLaterReport(order, existing) ? order : existing;

                    ChannelOrderHeaderFacts mergedHeader = ChannelOrderHeaderFacts.Reduce(
                        mergedLines,
                        new[] { existing.OrderDate, order.OrderDate });

                    dayByOrderKey[order.ExternalOrderId] = headerSource with
                    {
                        Lines = mergedLines,
                        ExternalStatus = mergedHeader.ExternalStatus,
                        CancelReason = mergedHeader.CancelReason,
                        OrderDate = mergedHeader.OrderDate,
                        StatusChangedAt = mergedHeader.StatusChangedAt,
                        ChannelReportedTotal = mergedHeader.ChannelReportedTotal,
                    };
                }
                else
                {
                    dayByOrderKey.Add(order.ExternalOrderId, order);
                }

                if (order.StatusChangedAt != null)
                {
                    DateTimeOffset observed = order.StatusChangedAt.Value;

                    if (maxObservedLastChangeDate == null || observed > maxObservedLastChangeDate.Value)
                    {
                        maxObservedLastChangeDate = observed;
                    }
                }
            }

            List<ChannelOrderSnapshot> dayOrders = dayByOrderKey.Values
                .OrderBy(order => order.ExternalOrderId, StringComparer.Ordinal)
                .ToList();

            await publishUnit(dayOrders, cancellationToken);

            totalOrders += dayOrders.Count;
            daysFetched++;
            firstDay ??= dayCursor;
            lastDay = dayCursor;

            dayCursor = dayCursor.AddDays(-1);
        }

        // The cursor moves only for the days actually fetched: dayCursor now holds the first day
        // this run did not fetch, which is exactly where the next run resumes. A throttled or
        // budget-stopped run therefore leaves that day unfetched rather than skipped.
        state.NextDayToFetch = dayCursor;
        state.MaxObservedLastChangeDate = maxObservedLastChangeDate;

        bool reachedScopeBoundary = dayCursor < oldestDay;

        if (reachedScopeBoundary)
        {
            state.IsCompleted = true;
            state.CompletedAt ??= DateTimeOffset.UtcNow;

            _logger.LogInformation(
                "JobRun {JobRunId}: backfill reached the scope boundary {OldestDay}, marking the walk complete",
                jobRunId,
                oldestDay);
        }

        return new BackfillOutcome
        {
            TotalOrders = totalOrders,
            DaysFetched = daysFetched,
            RowsWithoutIdentity = rowsWithoutIdentity,
            StoppedByThrottle = stoppedByThrottle,
            StoppedByBudget = stoppedByBudget,
            FirstDay = firstDay,
            LastDay = lastDay,
            IsStateChanged = daysFetched > 0 || reachedScopeBoundary,
        };
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
        Guid connectionId,
        OrdersRequest request,
        Guid jobRunId,
        CancellationToken cancellationToken)
    {
        Dictionary<string, ChannelOrderSnapshot> ordersByOrderKey = new Dictionary<string, ChannelOrderSnapshot>(StringComparer.Ordinal);

        _requestsIssued = 0;
        _rowsWithoutIdentity = 0;

        await FetchWindowAsync(ordersSource, connectionId, request, jobRunId, ordersByOrderKey, cancellationToken);

        return new OrdersFetchResult
        {
            Orders = ordersByOrderKey.Values.ToList(),
            RequestsIssued = _requestsIssued,
            RowsWithoutIdentity = _rowsWithoutIdentity,
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
    /// Rows the source could not identify fail the window outright: the dictionary only merges rows
    /// that agree on identity, and an unidentified row has none to agree on.
    /// </summary>
    private async Task FetchWindowAsync(
        IOrdersSource ordersSource,
        Guid connectionId,
        OrdersRequest request,
        Guid jobRunId,
        Dictionary<string, ChannelOrderSnapshot> ordersByOrderKey,
        CancellationToken cancellationToken)
    {
        if (_requestsIssued >= MaxRequestsPerRun)
        {
            throw new InvalidOperationException(
                $"Orders import exceeded {MaxRequestsPerRun} requests for JobRun {jobRunId}.");
        }

        _requestsIssued++;

        OrdersPage ordersPage = await ordersSource.GetOrdersAsync(connectionId, request, cancellationToken);

        if (ordersPage.RowsWithoutIdentity > 0)
        {
            // A page that dropped rows cannot be imported partially: the missing orders would be
            // indistinguishable from orders that never existed, and advancing the watermark past
            // this window would hide them from every later delta run as well. Failing here reuses
            // the same fail-fast stance as the unresolvable partial response below, and lets the
            // next scheduled run retry the window once the source data is complete.
            _rowsWithoutIdentity += ordersPage.RowsWithoutIdentity;

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

            // "Which report is newer" and "which lines we have" are two independent questions.
            // IsLaterReport below answers only the first, and it decides the fields that are neither
            // derived from lines nor re-derivable after the merge (warehouse, region, substatus).
            // The line union happens regardless of its result, because the newer report frequently
            // carries a subset of the cart. A bisection splits carts, so half A can hold unit 1 and
            // half B unit 2 of one gNumber; letting the winner's Lines replace the accumulated set
            // would silently drop whichever unit the older half contributed.
            bool isIncomingLater = IsLaterReport(order, existing);

            List<ChannelOrderLineSnapshot> mergedLines = UnionLines(existing.Lines, order.Lines);
            ChannelOrderSnapshot headerSource = isIncomingLater ? order : existing;

            // The header is reduced from the merged line set, never taken from the winning report:
            // either half may hold the only cancelled unit of a cart, or the only live one, and the
            // winning report's own header describes the units it happened to carry. The placement
            // instant goes through the same reduction, so the merged cart keeps the earliest instant
            // any half reported.
            ChannelOrderHeaderFacts mergedHeader = ChannelOrderHeaderFacts.Reduce(
                mergedLines,
                new[] { existing.OrderDate, order.OrderDate });

            ordersByOrderKey[order.ExternalOrderId] = headerSource with
            {
                Lines = mergedLines,
                ExternalStatus = mergedHeader.ExternalStatus,
                CancelReason = mergedHeader.CancelReason,
                OrderDate = mergedHeader.OrderDate,
                StatusChangedAt = mergedHeader.StatusChangedAt,
                ChannelReportedTotal = mergedHeader.ChannelReportedTotal
            };
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
            // Accepting the truncated response would silently drop orders, which is the defect this
            // path exists to fix; the run fails instead so the operator sees a window granularity
            // that the endpoint cannot serve rather than a partial import reported as success.
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

        await FetchWindowAsync(ordersSource, connectionId, firstHalf, jobRunId, ordersByOrderKey, cancellationToken);
        await FetchWindowAsync(ordersSource, connectionId, secondHalf, jobRunId, ordersByOrderKey, cancellationToken);
    }

    /// <summary>
    /// Unions the lines of two reports of one order, keyed by
    /// <see cref="ChannelOrderLineSnapshot.LineKey"/>. An unseen key is appended, keeping the lines
    /// already known; a key present on both sides keeps the version whose own
    /// <see cref="ChannelOrderLineSnapshot.StatusChangedAt"/> is later, and keeps the accumulated
    /// version on an exact tie, so the result does not depend on arrival order. The comparison is per
    /// line rather than per report: the order-level instant says nothing about which version of one
    /// unit is fresher, and two reports that split a cart usually do not carry the same unit at all.
    /// Returning a fresh list rather than mutating either argument is what lets
    /// <see cref="ChannelOrderSnapshot"/> stay an immutable record without making Lines settable.
    /// </summary>
    private static List<ChannelOrderLineSnapshot> UnionLines(
        List<ChannelOrderLineSnapshot> existing,
        List<ChannelOrderLineSnapshot> incoming)
    {
        List<ChannelOrderLineSnapshot> merged = new List<ChannelOrderLineSnapshot>(existing);

        foreach (ChannelOrderLineSnapshot line in incoming)
        {
            int index = merged.FindIndex(
                existingLine => string.Equals(existingLine.LineKey, line.LineKey, StringComparison.Ordinal));

            if (index < 0)
            {
                merged.Add(line);
                continue;
            }

            if (IsLaterLine(line, merged[index]))
            {
                merged[index] = line;
            }
        }

        return merged;
    }

    /// <summary>
    /// Orders whether <paramref name="candidate"/> is a later version of one line than
    /// <paramref name="current"/>. A version reporting no instant never displaces one that reports
    /// an instant, and equal instants resolve in favour of the accumulated version, so replaying a
    /// report cannot flip the stored facts back and forth.
    /// </summary>
    private static bool IsLaterLine(ChannelOrderLineSnapshot candidate, ChannelOrderLineSnapshot current)
    {
        DateTimeOffset? candidateInstant = candidate.StatusChangedAt;
        DateTimeOffset? currentInstant = current.StatusChangedAt;

        if (candidateInstant == null)
        {
            return false;
        }

        if (currentInstant == null)
        {
            return true;
        }

        return candidateInstant.Value > currentInstant.Value;
    }

    /// <summary>
    /// Derives the next delta watermark from what the endpoint actually selected on:
    /// <c>lastChangeDate</c> for a flag=0 run. A row that reports no <c>lastChangeDate</c>
    /// (unparseable or absent) contributes nothing, and <c>OrderDate</c> is never substituted —
    /// it lives on a different time axis than the one the endpoint filters by, so folding it in
    /// would push the watermark past the served instant and drop the row from every later delta
    /// run. The result is clamped to the run's own window end, since nothing the run fetched can
    /// justify a later watermark; the checkpointed <paramref name="windowTo"/> makes that clamp
    /// stable across redelivery.
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

    private static List<List<ChannelOrderSnapshot>> SplitIntoBatches(List<ChannelOrderSnapshot> orders)
    {
        List<List<ChannelOrderSnapshot>> batches = new List<List<ChannelOrderSnapshot>>();

        for (int i = 0; i < orders.Count; i += BatchSize)
        {
            batches.Add(orders.Skip(i).Take(BatchSize).ToList());
        }

        // An empty terminal batch still flows through Sales so the aggregator finalises the run.
        if (batches.Count == 0)
        {
            batches.Add(new List<ChannelOrderSnapshot>());
        }

        return batches;
    }

    /// <summary>
    /// Orders whether <paramref name="candidate"/> is a later report of the same order than
    /// <paramref name="current"/>. <c>StatusChangedAt</c> is the channel's own "as of" instant and
    /// decides; <c>OrderDate</c> breaks the tie where a channel reports no change time, and an
    /// exact tie keeps the current report so the merge does not depend on arrival order. Note that
    /// the snapshots are records whose <c>Lines</c> is a list, so value equality would be
    /// reference-based anyway — comparing the instants is both correct and order-independent.
    /// </summary>
    private static bool IsLaterReport(ChannelOrderSnapshot candidate, ChannelOrderSnapshot current)
    {
        DateTimeOffset candidateInstant = candidate.StatusChangedAt ?? candidate.OrderDate;
        DateTimeOffset currentInstant = current.StatusChangedAt ?? current.OrderDate;

        return candidateInstant > currentInstant;
    }

    private static OrdersImportCheckpointPayload? DeserializePayload(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<OrdersImportCheckpointPayload>(payload);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task FailJobAsync(JobRun jobRun, string error)
    {
        jobRun.Status = JobStatus.Failed;
        jobRun.Error = error;
        jobRun.CompletedAt = DateTimeOffset.UtcNow;

        // The consumer token may already be cancelled; the failure must still be persisted.
        await _jobRunRepository.UpdateAsync(jobRun, CancellationToken.None);
    }
}
