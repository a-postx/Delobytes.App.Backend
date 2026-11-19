using Delobytes.App.Backend.Finance.Domain.Enums;

namespace Delobytes.App.Backend.Finance.Application.Queries;

/// <summary>
/// Aging of an open expectation. Buckets are measured from the deadline, not from the sale date, so
/// the queue shows how late a document is rather than how old the sale is.
/// </summary>
internal static class PnlExpectationAging
{
    /// <summary>
    /// Whole days the document is past its deadline. Negative while the window is still open.
    /// </summary>
    /// <param name="expectedBy">Deadline of the expectation.</param>
    /// <param name="now">Current instant.</param>
    /// <returns>Whole days past the deadline.</returns>
    public static int DaysOverdue(DateTimeOffset expectedBy, DateTimeOffset now)
    {
        return (int)Math.Floor((now - expectedBy).TotalDays);
    }

    /// <summary>
    /// Aging bucket of an expectation.
    /// </summary>
    /// <param name="state">Expectation state.</param>
    /// <param name="daysOverdue">Whole days past the deadline.</param>
    /// <returns>The bucket name.</returns>
    public static string BucketOf(PnlExpectationState state, int daysOverdue)
    {
        if (state is PnlExpectationState.ClosedByDocument or PnlExpectationState.ClosedWithException)
        {
            return "closed";
        }

        if (daysOverdue < 0)
        {
            return "notDue";
        }

        if (daysOverdue <= 7)
        {
            return "0-7";
        }

        if (daysOverdue <= 14)
        {
            return "8-14";
        }

        return daysOverdue <= 30 ? "15-30" : "30+";
    }
}
