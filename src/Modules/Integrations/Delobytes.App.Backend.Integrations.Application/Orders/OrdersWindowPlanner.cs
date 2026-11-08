using Delobytes.App.Backend.Integrations.Application.Options;
using Delobytes.App.Backend.Integrations.Contracts.Models;
using Delobytes.App.Backend.Integrations.Domain.Entities;

namespace Delobytes.App.Backend.Integrations.Application.Orders;

/// <summary>
/// Pure calendar and watermark arithmetic for an orders import run. Has no dependencies, so it is
/// unit-testable without mocks.
/// </summary>
internal static class OrdersWindowPlanner
{
    public static readonly TimeSpan MoscowTime = TimeSpan.FromHours(3);

    public static DateTimeOffset GetMoscowDayStart(DateTimeOffset instant)
    {
        DateTimeOffset moscow = instant.ToOffset(MoscowTime);

        return new DateTimeOffset(
            moscow.Year,
            moscow.Month,
            moscow.Day,
            0,
            0,
            0,
            MoscowTime);
    }

    /// <summary>
    /// Resolves the delta window start. <see cref="DeltaWindow.UsedLegacyFallback"/> is set when
    /// there is no SyncCheckpoint and the backfill state has no handover watermark either, so the
    /// caller can log the legacy-fallback warning itself.
    /// </summary>
    public static DeltaWindow ResolveDeltaWindowFrom(
        SyncCheckpoint? syncCheckpoint,
        OrdersBackfillState backfillState)
    {
        if (syncCheckpoint?.LastChangedAt != null)
        {
            return new DeltaWindow(
                syncCheckpoint.LastChangedAt.Value - WildberriesImportOptions.DefaultSafetyOverlap,
                UsedLegacyFallback: false);
        }

        DateTimeOffset windowFrom = backfillState.HandoverWatermark
            ?? backfillState.OldestDayInScope + WildberriesImportOptions.DefaultBackfillWindow;

        return new DeltaWindow(windowFrom, UsedLegacyFallback: !backfillState.HandoverWatermark.HasValue);
    }

    /// <summary>
    /// Derives the next delta watermark from what the endpoint actually selected on:
    /// <c>lastChangeDate</c> for a flag=0 run. <see cref="WatermarkResolution.ObservedBeyondWindow"/>
    /// carries the clamped value when the observed watermark is later than the run window end, so
    /// the caller can log it.
    /// </summary>
    public static WatermarkResolution GetMaxChangedAt(
        IEnumerable<ChannelOrderSnapshot> orders, DateTimeOffset windowTo)
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
            return new WatermarkResolution(windowTo, max.Value);
        }

        return new WatermarkResolution(max?.ToUniversalTime(), ObservedBeyondWindow: null);
    }

    /// <summary>
    /// Derives the next delta watermark for a channel that selects on the placement instant
    /// (<c>created_at</c>, carried as <see cref="ChannelOrderSnapshot.OrderDate"/>). Using the
    /// change instant here would let the watermark jump past postings the window never returned:
    /// the channel would then never ask for them again.
    /// <see cref="WatermarkResolution.ObservedBeyondWindow"/> carries the clamped value when the
    /// newest placement is later than the run window end, so the caller can log it.
    /// </summary>
    public static WatermarkResolution GetMaxCreatedAt(
        IEnumerable<ChannelOrderSnapshot> orders, DateTimeOffset windowTo)
    {
        DateTimeOffset? max = null;

        foreach (ChannelOrderSnapshot order in orders)
        {
            DateTimeOffset value = order.OrderDate;

            if (max == null || value > max.Value)
            {
                max = value;
            }
        }

        if (max.HasValue && max.Value > windowTo)
        {
            return new WatermarkResolution(windowTo.ToUniversalTime(), max.Value);
        }

        return new WatermarkResolution(max?.ToUniversalTime(), ObservedBeyondWindow: null);
    }
}

internal sealed record DeltaWindow(DateTimeOffset From, bool UsedLegacyFallback);

internal sealed record WatermarkResolution(
    DateTimeOffset? Value, DateTimeOffset? ObservedBeyondWindow);
