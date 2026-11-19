using Delobytes.App.Backend.Finance.Domain.Enums;

namespace Delobytes.App.Backend.Finance.Application.Expectation;

/// <summary>
/// The fixed rules of the expected-document lifecycle: how long a document may take, what counts as
/// immaterial, and how the deadline is derived. Constants, not options: the window and the threshold
/// are a product decision, and making them configurable per tenant would let two tenants report the
/// same channel differently.
/// </summary>
public static class ExpectationPolicy
{
    /// <summary>
    /// How long after the reported period end a document is still expected.
    /// </summary>
    public static readonly TimeSpan Window = TimeSpan.FromDays(14);

    /// <summary>
    /// The largest estimated impact that is closed automatically as immaterial.
    /// </summary>
    public const decimal MaterialityThreshold = 10m;

    /// <summary>
    /// How far back the comparable history for the impact estimate is taken from.
    /// </summary>
    public const int EstimateLookbackDays = 90;

    /// <summary>
    /// The minimum number of comparable lines before an estimate is considered meaningful.
    /// </summary>
    public const int MinimumComparableLines = 3;

    /// <summary>
    /// The only currency the materiality threshold is expressed in. An expectation in another
    /// currency is never closed automatically, because 10 RUB is not 10 of anything else.
    /// </summary>
    public const string MaterialityCurrency = "RUB";

    /// <summary>
    /// Closure reason recorded when the system closed an expectation as immaterial.
    /// </summary>
    public const string ImmaterialClosureReason = "Immaterial";

    /// <summary>
    /// Computes the deadline of a document of the sale reported up to <paramref name="reportPeriodTo"/>.
    /// Anchored to the period end, not to the sale date: a weekly report describes days that are
    /// already over, and the delivery document belongs to the same period.
    /// </summary>
    /// <param name="reportPeriodTo">Last day of the reporting period of the sale.</param>
    /// <returns>The instant the document becomes overdue.</returns>
    public static DateTimeOffset DeadlineOf(DateOnly reportPeriodTo)
    {
        DateTimeOffset periodEnd = new DateTimeOffset(
            reportPeriodTo.ToDateTime(TimeOnly.MinValue),
            TimeSpan.Zero);

        return periodEnd.Add(Window);
    }

    /// <summary>
    /// Whether the expectation still waits for its document.
    /// </summary>
    /// <param name="state">Expectation state.</param>
    /// <returns>True for <see cref="PnlExpectationState.Expected"/> and <see cref="PnlExpectationState.Overdue"/>.</returns>
    public static bool IsOpen(PnlExpectationState state)
    {
        return state is PnlExpectationState.Expected or PnlExpectationState.Overdue;
    }

    /// <summary>
    /// Whether the estimated impact is small enough to close without an administrator.
    /// </summary>
    /// <param name="estimate">Estimated impact, or null when there is nothing to compare against.</param>
    /// <param name="currency">Currency of the line.</param>
    /// <returns>True when the expectation may be closed as immaterial.</returns>
    public static bool IsImmaterial(decimal? estimate, string currency)
    {
        return estimate.HasValue
            && Math.Abs(estimate.Value) <= MaterialityThreshold
            && string.Equals(currency, MaterialityCurrency, StringComparison.Ordinal);
    }
}
