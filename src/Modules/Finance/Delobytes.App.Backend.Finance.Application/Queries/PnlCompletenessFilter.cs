using Delobytes.App.Backend.Finance.Domain.Enums;

namespace Delobytes.App.Backend.Finance.Application.Queries;

/// <summary>
/// Reads the completeness selection of a read query. Shared by the line and the summary queries so
/// both apply the same default and both accept the same scopes.
/// </summary>
/// <remarks>
/// An omitted filter means <c>final</c>: the lines whose figures are not provisional, which includes
/// the lines closed with an exception. Defaulting to <c>complete</c> alone would hide exactly the
/// lines the expectation aging pass resolved as immaterial. <c>all</c> removes the restriction, so a
/// caller that needs the full picture with the provisional part does not have to query each state
/// separately and merge the aggregates itself.
/// </remarks>
public static class PnlCompletenessFilter
{
    /// <summary>Scope of every completeness state.</summary>
    public const string All = "all";

    /// <summary>Scope of the states whose figures are final: complete and closed with exception.</summary>
    public const string Final = "final";

    /// <summary>Scope of the states whose figures are provisional: provisional and overdue.</summary>
    public const string Provisional = "provisional";

    /// <summary>Exact state: every expected document arrived.</summary>
    public const string Complete = "complete";

    /// <summary>Exact state: an expected document is past its window and still open.</summary>
    public const string Overdue = "overdue";

    /// <summary>Exact state: the missing document was closed with a recorded exception.</summary>
    public const string ClosedWithException = "closedWithException";

    private static readonly IReadOnlyList<PnlLineCompleteness> FinalStates =
        new[] { PnlLineCompleteness.Complete, PnlLineCompleteness.ClosedWithException };

    private static readonly IReadOnlyList<PnlLineCompleteness> ProvisionalStates =
        new[] { PnlLineCompleteness.Provisional, PnlLineCompleteness.Overdue };

    private static readonly IReadOnlyList<PnlLineCompleteness> CompleteStates =
        new[] { PnlLineCompleteness.Complete };

    private static readonly IReadOnlyList<PnlLineCompleteness> OverdueStates =
        new[] { PnlLineCompleteness.Overdue };

    private static readonly IReadOnlyList<PnlLineCompleteness> ClosedWithExceptionStates =
        new[] { PnlLineCompleteness.ClosedWithException };

    /// <summary>Every value <see cref="TryParse"/> accepts, for validation messages.</summary>
    public static IReadOnlyList<string> AcceptedValues { get; } = new[]
    {
        All,
        Final,
        Provisional,
        Complete,
        Overdue,
        ClosedWithException,
    };

    /// <summary>
    /// Parses the requested completeness selection. An omitted value means <c>final</c> and
    /// <c>all</c> yields a null set, which the read store treats as no restriction.
    /// </summary>
    /// <param name="raw">Raw value from the query string, or null.</param>
    /// <param name="states">States to include, or null for every state.</param>
    /// <returns><c>false</c> when the value is not one of <see cref="AcceptedValues"/>.</returns>
    public static bool TryParse(string? raw, out IReadOnlyList<PnlLineCompleteness>? states)
    {
        states = FinalStates;

        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        string value = raw.Trim();

        if (value.Equals(All, StringComparison.OrdinalIgnoreCase))
        {
            states = null;
            return true;
        }

        if (value.Equals(Final, StringComparison.OrdinalIgnoreCase))
        {
            states = FinalStates;
            return true;
        }

        // A scope, not the exact Provisional state: both open states share the same "figures are
        // not final yet" meaning, and separating them is what the overdue scope is for.
        if (value.Equals(Provisional, StringComparison.OrdinalIgnoreCase))
        {
            states = ProvisionalStates;
            return true;
        }

        if (value.Equals(Complete, StringComparison.OrdinalIgnoreCase))
        {
            states = CompleteStates;
            return true;
        }

        if (value.Equals(Overdue, StringComparison.OrdinalIgnoreCase))
        {
            states = OverdueStates;
            return true;
        }

        if (value.Equals(ClosedWithException, StringComparison.OrdinalIgnoreCase))
        {
            states = ClosedWithExceptionStates;
            return true;
        }

        states = null;
        return false;
    }

    /// <summary>
    /// Parses the requested completeness selection, treating an unknown value as a caller error.
    /// The API validates the parameter first, so a rejected value here means a bug in the caller.
    /// </summary>
    /// <param name="raw">Raw value from the query string, or null.</param>
    /// <returns>States to include, or null for every state.</returns>
    public static IReadOnlyList<PnlLineCompleteness>? Parse(string? raw)
    {
        if (TryParse(raw, out IReadOnlyList<PnlLineCompleteness>? states))
        {
            return states;
        }

        throw new ArgumentException(
            $"Unknown completeness filter '{raw}'. Accepted values: {string.Join(", ", AcceptedValues)}.",
            nameof(raw));
    }
}
