using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Contracts.Models;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers;

/// <summary>
/// Per-connection backfill cursor and the day-by-day walk that advances it, publishing one unit
/// of orders per calendar day.
/// </summary>
internal sealed class OrdersBackfillWalker
{
    private readonly PacedOrdersSource _pacedSource;
    private readonly IOrdersBackfillStateRepository _backfillStateRepository;
    private readonly ILogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _backfillWindow;
    private readonly TimeSpan _safetyOverlap;

    public OrdersBackfillWalker(
        PacedOrdersSource pacedSource,
        IOrdersBackfillStateRepository backfillStateRepository,
        ILogger logger,
        TimeProvider timeProvider,
        TimeSpan backfillWindow,
        TimeSpan safetyOverlap)
    {
        _pacedSource = pacedSource;
        _backfillStateRepository = backfillStateRepository;
        _logger = logger;
        _timeProvider = timeProvider;
        _backfillWindow = backfillWindow;
        _safetyOverlap = safetyOverlap;
    }

    /// <summary>
    /// Loads the per-connection backfill cursor, creating it on first contact. A fresh cursor starts
    /// at today 00:00 MSK and walks backwards to today - <see cref="_backfillWindow"/>, which is the
    /// scope the single-request backfill used to cover.
    /// </summary>
    public async Task<OrdersBackfillState> GetOrCreateAsync(Guid connectionId, CancellationToken cancellationToken)
    {
        OrdersBackfillState? state = await _backfillStateRepository.GetAsync(connectionId, cancellationToken);

        if (state != null)
        {
            return state;
        }

        DateTimeOffset todayStart = OrdersWindowPlanner.GetMoscowDayStart(_timeProvider.GetUtcNow());

        state = new OrdersBackfillState
        {
            Id = Guid.NewGuid(),
            ConnectionId = connectionId,
            NextDayToFetch = todayStart,
            OldestDayInScope = todayStart - _backfillWindow,
            IsCompleted = false,
        };

        await _backfillStateRepository.UpsertAsync(state, cancellationToken);

        return state;
    }

    public async Task<BackfillOutcome> FetchBackfillDaysAsync(
        IOrdersSource ordersSource,
        Connection connection,
        OrdersBackfillState state,
        Guid jobRunId,
        OrdersImportRunState runState,
        Func<List<ChannelOrderSnapshot>, CancellationToken, Task> publishUnit,
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
            DateTimeOffset retentionStart = OrdersWindowPlanner.GetMoscowDayStart(now - _backfillWindow);
            DateTimeOffset oldestDay = state.OldestDayInScope > retentionStart
                ? state.OldestDayInScope : retentionStart;

            if (!state.CatchUpNextDay.HasValue && state.NextDayToFetch < oldestDay)
            {
                int skippedDays = Math.Max(0, (int)(state.NextDayToFetch - state.OldestDayInScope).TotalDays + 1);
                if (skippedDays > 0)
                {
                    _logger.LogWarning("JobRun {JobRunId}: skipped {SkippedDays} backfill day(s) outside retention", jobRunId, skippedDays);
                }

                state.CatchUpNextDay = OrdersWindowPlanner.GetMoscowDayStart(state.OldestDayInScope + _backfillWindow);
                await _backfillStateRepository.UpsertAsync(state, cancellationToken);
            }

            bool isCatchUp = state.CatchUpNextDay.HasValue;
            DateTimeOffset day = OrdersWindowPlanner.GetMoscowDayStart(isCatchUp ? state.CatchUpNextDay.GetValueOrDefault() : state.NextDayToFetch);

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
                page = await _pacedSource.GetOrdersAsync(ordersSource, connection, request, runState, cancellationToken);
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
                if (day >= OrdersWindowPlanner.GetMoscowDayStart(_timeProvider.GetUtcNow()))
                {
                    state.HandoverWatermark = runState.LastRequestStartedAt - _safetyOverlap;
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
}
