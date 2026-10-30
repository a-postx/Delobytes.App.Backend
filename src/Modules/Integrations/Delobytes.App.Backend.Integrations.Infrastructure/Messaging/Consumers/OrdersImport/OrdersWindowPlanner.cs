using Delobytes.App.Backend.Integrations.Contracts.Models;
using Delobytes.App.Backend.Integrations.Domain.Entities;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers.OrdersImport;

/// <summary>
/// Pure calendar and watermark arithmetic for an orders import run. Has no dependencies, so it is
/// unit-testable without mocks.
/// </summary>
internal static class OrdersWindowPlanner
{
    public static readonly TimeSpan MoscowOffset = OrdersImportConstants.MoscowOffset;

    public static DateTimeOffset GetMoscowDayStart(DateTimeOffset instant)
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

    /// <summary>
    /// Resolves the delta window start. <paramref name="usedLegacyFallback"/> is set when there is
    /// no SyncCheckpoint and the backfill state has no handover watermark either, so the caller can
    /// log the legacy-fallback warning itself.
    /// </summary>
    public static DateTimeOffset ResolveDeltaWindowFrom(
        SyncCheckpoint? syncCheckpoint,
        OrdersBackfillState backfillState,
        out bool usedLegacyFallback)
    {
        if (syncCheckpoint?.LastChangedAt != null)
        {
            usedLegacyFallback = false;
            return syncCheckpoint.LastChangedAt.Value - OrdersImportConstants.SafetyOverlap;
        }

        DateTimeOffset windowFrom = backfillState.HandoverWatermark
            ?? backfillState.OldestDayInScope + OrdersImportConstants.BackfillWindow;

        usedLegacyFallback = !backfillState.HandoverWatermark.HasValue;

        return windowFrom;
    }

    /// <summary>
    /// Derives the next delta watermark from what the endpoint actually selected on:
    /// <c>lastChangeDate</c> for a flag=0 run. <paramref name="observedBeyondWindow"/> carries the
    /// clamped value when the observed watermark is later than the run window end, so the caller
    /// can log it.
    /// </summary>
    public static DateTimeOffset? GetMaxChangedAt(
        IEnumerable<ChannelOrderSnapshot> orders,
        DateTimeOffset windowTo,
        out DateTimeOffset? observedBeyondWindow)
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
            observedBeyondWindow = max.Value;
            return windowTo;
        }

        observedBeyondWindow = null;
        return max?.ToUniversalTime();
    }
}
