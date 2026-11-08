using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Application.Options;
using Delobytes.App.Backend.Integrations.Contracts.Models;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Microsoft.Extensions.Logging;
namespace Delobytes.App.Backend.Integrations.Application.Orders;

/// <summary>
/// Per-connection backfill cursor and the day-by-day walk that advances it, publishing one unit
/// of orders per calendar day.
/// </summary>
public sealed class OrdersBackfillWalker
{
    private readonly IOrdersBackfillStateRepository _backfillStateRepository;
    private readonly OrdersBatchPublisher _batchPublisher;
    private readonly ILogger<OrdersBackfillWalker> _logger;
    private readonly TimeProvider _timeProvider;

    public OrdersBackfillWalker(
        IOrdersBackfillStateRepository backfillStateRepository,
        OrdersBatchPublisher batchPublisher,
        ILogger<OrdersBackfillWalker> logger,
        TimeProvider timeProvider)
    {
        _backfillStateRepository = backfillStateRepository;
        _batchPublisher = batchPublisher;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Loads the per-connection, per-job-type backfill cursor, creating it on first contact. A
    /// fresh cursor starts at today 00:00 MSK and walks backwards to today - <paramref name="tuning"/>'s
    /// backfill window, which is the scope the single-request backfill used to cover.
    /// </summary>
    public async Task<OrdersBackfillState> GetOrCreateAsync(
        Guid connectionId,
        JobType jobType,
        OrdersImportTuning tuning,
        CancellationToken cancellationToken)
    {
        OrdersBackfillState? state = await _backfillStateRepository.GetAsync(connectionId, jobType, cancellationToken);

        if (state != null)
        {
            return state;
        }

        DateTimeOffset todayStart = OrdersWindowPlanner.GetMoscowDayStart(_timeProvider.GetUtcNow());

        state = new OrdersBackfillState
        {
            Id = Guid.NewGuid(),
            ConnectionId = connectionId,
            JobType = jobType,
            NextDayToFetch = todayStart,
            OldestDayInScope = todayStart - tuning.BackfillWindow,
            IsCompleted = false,
        };

        await _backfillStateRepository.UpsertAsync(state, cancellationToken);

        return state;
    }

    internal async Task<FetchOutcome> FetchBackfillDaysAsync(
        OrdersImportContext context,
        RunCounters counters,
        BatchSequence batches,
        IOrdersWindowReader windowReader,
        CancellationToken cancellationToken)
    {
        Guid jobRunId = context.JobRun.Id;
        int daysFetched = 0;
        int totalOrders = 0;
        DateTimeOffset? firstDay = null;
        DateTimeOffset? lastDay = null;
        FetchOutcome.Stopped? stop = null;
        OrdersBackfillState state = context.BackfillState;
        OrdersImportTuning tuning = context.Tuning;

        while (!state.IsCompleted)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DateTimeOffset now = _timeProvider.GetUtcNow();
            DateTimeOffset retentionStart = OrdersWindowPlanner.GetMoscowDayStart(now - tuning.BackfillWindow);
            DateTimeOffset oldestDay = state.OldestDayInScope > retentionStart
                ? state.OldestDayInScope : retentionStart;

            if (!state.CatchUpNextDay.HasValue && state.NextDayToFetch < oldestDay)
            {
                int skippedDays = Math.Max(0, (int)(state.NextDayToFetch - state.OldestDayInScope).TotalDays + 1);
                if (skippedDays > 0)
                {
                    _logger.LogWarning("JobRun {JobRunId}: skipped {SkippedDays} backfill day(s) outside retention", jobRunId, skippedDays);
                }

                state.CatchUpNextDay = OrdersWindowPlanner.GetMoscowDayStart(state.OldestDayInScope + tuning.BackfillWindow);
                await _backfillStateRepository.UpsertAsync(state, cancellationToken);
            }

            bool isCatchUp = state.CatchUpNextDay.HasValue;
            DateTimeOffset day = OrdersWindowPlanner.GetMoscowDayStart(isCatchUp ? state.CatchUpNextDay.GetValueOrDefault() : state.NextDayToFetch);

            OrdersRequest request = new OrdersRequest
            {
                DateFrom = day,
                DateTo = day.AddDays(1).AddSeconds(-1),
                Selection = OrdersSelectionMode.Placed,
                Limit = 0,
                Schema = context.Schema,
            };

            FetchOutcome windowOutcome = await windowReader.ReadWindowAsync(request, context, counters, cancellationToken);

            if (windowOutcome is FetchOutcome.Stopped stopped)
            {
                stop = stopped;
                break;
            }

            FetchOutcome.DeltaRead windowRead = (FetchOutcome.DeltaRead)windowOutcome;
            List<ChannelOrderSnapshot> dayOrders = OrderSnapshotMerger.MergeByExternalOrderId(windowRead.Orders);

            await _batchPublisher.PublishUnitAsync(
                context,
                counters,
                batches,
                dayOrders.OrderBy(order => order.ExternalOrderId, StringComparer.Ordinal).ToList(),
                cancellationToken);

            totalOrders += dayOrders.Count;
            daysFetched++;
            firstDay ??= day;
            lastDay = day;

            if (isCatchUp)
            {
                if (day >= OrdersWindowPlanner.GetMoscowDayStart(_timeProvider.GetUtcNow()))
                {
                    state.HandoverWatermark = counters.LastRequestStartedAt - tuning.SafetyOverlap;
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

            await _backfillStateRepository.UpsertAsync(state, cancellationToken);
        }

        _logger.LogInformation(
            "JobRun {JobRunId}: branch=backfill, days fetched={DaysFetched}, first day={FirstDay}, "
            + "last day={LastDay}, rows={Rows}, stopCause={StopCause}, "
            + "nextDayToFetch={NextDayToFetch}, isCompleted={IsCompleted}",
            jobRunId,
            daysFetched,
            firstDay,
            lastDay,
            totalOrders,
            stop?.Cause,
            state.NextDayToFetch,
            state.IsCompleted);

        if (stop != null)
        {
            return new FetchOutcome.Stopped
            {
                RequestsIssued = counters.RequestsIssued,
                Cause = stop.Cause,
                ResumeAt = stop.ResumeAt,
            };
        }

        return new FetchOutcome.BackfillRead
        {
            RequestsIssued = counters.RequestsIssued,
            OrdersFetched = totalOrders,
            IsCompleted = state.IsCompleted,
            HandoverWatermark = state.IsCompleted ? state.HandoverWatermark : null,
        };
    }
}
