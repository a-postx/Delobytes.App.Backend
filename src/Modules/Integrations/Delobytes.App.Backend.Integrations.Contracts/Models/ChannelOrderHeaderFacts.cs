namespace Delobytes.App.Backend.Integrations.Contracts.Models;

/// <summary>
/// Cart-level fields that <see cref="ChannelOrderSnapshot"/> derives from its lines rather than
/// reads from the payload. Produced by <see cref="Reduce"/> and by nothing else, so a cart assembled
/// from several reports - or from several runs - cannot end up describing a header that contradicts
/// its own line set.
/// </summary>
/// <remarks>
/// Lives in the contracts assembly rather than next to the mapper because the reduction has to run
/// again after the lines are merged (in the orchestrator) and again after they are persisted (in
/// Sales), and Sales does not reference the Integrations infrastructure.
/// </remarks>
public sealed record ChannelOrderHeaderFacts
{
    /// <summary>
    /// Gets the cart status reduced from the lines. A cart is cancelled only when every unit is.
    /// </summary>
    public string ExternalStatus { get; init; } = default!;

    /// <summary>
    /// Gets the cart cancel reason, set only for a cart every unit of which is cancelled.
    /// </summary>
    public string? CancelReason { get; init; }

    /// <summary>
    /// Gets the earliest placement instant over the rows that contributed to the cart.
    /// </summary>
    public DateTimeOffset OrderDate { get; init; }

    /// <summary>
    /// Gets the latest per-unit change instant, or null when no unit reported one.
    /// </summary>
    public DateTimeOffset? StatusChangedAt { get; init; }

    /// <summary>
    /// Gets the amount the channel reported for the cart, set only when the cart holds exactly one
    /// line. Never summed: the payload has no cart-level total, so a sum would be a derived value
    /// in a reported-facts field.
    /// </summary>
    public decimal? ChannelReportedTotal { get; init; }

    /// <summary>
    /// Reduces a final line set into the cart-level fields.
    /// </summary>
    /// <param name="lines">The whole line set of the cart, after every merge.</param>
    /// <param name="rowOrderDates">
    /// Every placement instant reported for the cart, from all reports that contributed to it.
    /// Kept separate from <paramref name="lines"/> because the line snapshot does not carry the
    /// placement time, and taking the minimum over each report's own minimum is the same as taking
    /// it over their rows.
    /// </param>
    /// <returns>The reduced cart-level fields.</returns>
    public static ChannelOrderHeaderFacts Reduce(
        IReadOnlyCollection<ChannelOrderLineSnapshot> lines,
        IEnumerable<DateTimeOffset> rowOrderDates)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(rowOrderDates);

        if (lines.Count == 0)
        {
            throw new InvalidOperationException("Cannot reduce cart-level fields from an empty line set.");
        }

        bool allCancelled = true;
        DateTimeOffset? statusChangedAt = null;
        decimal? channelReportedTotal = null;
        int lineCount = 0;

        foreach (ChannelOrderLineSnapshot line in lines)
        {
            lineCount++;

            allCancelled = allCancelled && line.IsCancelled;

            if (line.StatusChangedAt != null
                && (statusChangedAt == null || line.StatusChangedAt.Value > statusChangedAt.Value))
            {
                statusChangedAt = line.StatusChangedAt;
            }

            if (lineCount == 1)
            {
                channelReportedTotal = line.ChannelReportedTotal;
            }
        }

        if (lineCount != 1)
        {
            channelReportedTotal = null;
        }

        DateTimeOffset? orderDate = null;

        foreach (DateTimeOffset rowOrderDate in rowOrderDates)
        {
            if (orderDate == null || rowOrderDate < orderDate.Value)
            {
                orderDate = rowOrderDate;
            }
        }

        if (orderDate == null)
        {
            throw new InvalidOperationException(
                "Cannot reduce cart-level fields: no report of this cart carried a parseable order date.");
        }

        return new ChannelOrderHeaderFacts
        {
            ExternalStatus = allCancelled ? "cancelled" : "active",
            CancelReason = allCancelled ? "cancel" : null,
            OrderDate = orderDate.Value,
            StatusChangedAt = statusChangedAt,
            ChannelReportedTotal = channelReportedTotal
        };
    }
}
