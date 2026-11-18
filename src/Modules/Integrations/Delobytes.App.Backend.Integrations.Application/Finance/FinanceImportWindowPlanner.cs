using Delobytes.App.Backend.Integrations.Application.SalesChannels;

namespace Delobytes.App.Backend.Integrations.Application.Finance;

/// <summary>
/// Computes the date window one finance import run covers. Pure, so the overlap and backfill rules
/// can be exercised without a clock or a database.
/// </summary>
public static class FinanceImportWindowPlanner
{
    /// <summary>
    /// Computes the window for a channel.
    /// </summary>
    /// <param name="channelCode">Channel code.</param>
    /// <param name="watermark">Last successfully imported date.</param>
    /// <param name="today">Today in the channel's own calendar.</param>
    /// <param name="initialBackfillDays">Deepest date the initial backfill may reach back to.</param>
    /// <param name="wbOverlapDays">Wildberries re-issue overlap.</param>
    /// <param name="ozonOverlapDays">Ozon overlap.</param>
    /// <returns>The window, or null when there is nothing due yet.</returns>
    public static (DateOnly From, DateOnly To)? Plan(
        string channelCode,
        DateOnly watermark,
        DateOnly today,
        int initialBackfillDays,
        int wbOverlapDays,
        int ozonOverlapDays)
    {
        DateOnly backfillStart = today.AddDays(-Math.Max(0, initialBackfillDays));
        DateOnly floor = watermark > backfillStart ? watermark : backfillStart;

        if (string.Equals(channelCode, ChannelCatalog.WildberriesCode, StringComparison.OrdinalIgnoreCase))
        {
            DateOnly from = floor.AddDays(-Math.Max(0, wbOverlapDays));

            if (from < backfillStart)
            {
                from = backfillStart;
            }

            return (from, today);
        }

        if (string.Equals(channelCode, ChannelCatalog.OzonCode, StringComparison.OrdinalIgnoreCase))
        {
            DateOnly to = today.AddDays(-1);
            DateOnly from = floor.AddDays(-Math.Max(0, ozonOverlapDays));

            if (from < backfillStart)
            {
                from = backfillStart;
            }

            return from > to ? null : (from, to);
        }

        return null;
    }
}
