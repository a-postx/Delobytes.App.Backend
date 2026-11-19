using Delobytes.App.Backend.Finance.Domain.Entities;
using Delobytes.App.Backend.Finance.Domain.Enums;

namespace Delobytes.App.Backend.Finance.Application.Projection;

/// <summary>
/// Reduces the expectations of one line to the completeness the line reports. Pure: the projection
/// writes the result, this decides it.
/// </summary>
/// <remarks>
/// Severity is ordered so a single overdue document outranks any number of pending ones: the queue
/// is worked from the top, and an overdue item must not hide behind a line that is merely waiting.
/// A closed-with-exception line is reported as such rather than as complete, because its figures
/// were accepted with a known gap.
/// </remarks>
public static class PnlLineCompletenessResolver
{
    /// <summary>
    /// Resolves the completeness of a line from its expectations.
    /// </summary>
    /// <param name="expectations">Every expectation of the line, open and closed.</param>
    /// <returns>The completeness, the outstanding document types and the earliest open deadline.</returns>
    public static PnlLineCompletenessResult Resolve(IEnumerable<PnlLineExpectation> expectations)
    {
        ArgumentNullException.ThrowIfNull(expectations);

        List<PnlLineExpectation> all = expectations.ToList();

        if (all.Count == 0)
        {
            return new PnlLineCompletenessResult(
                PnlLineCompleteness.Complete,
                Array.Empty<string>(),
                null);
        }

        List<PnlLineExpectation> open = all
            .Where(e => Finance.Application.Expectation.ExpectationPolicy.IsOpen(e.State))
            .ToList();

        List<string> missing = open
            .Select(e => e.ExpectedType.ToString())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        DateTimeOffset? expectedBy = open.Count == 0
            ? null
            : open.Min(e => e.ExpectedBy);

        PnlLineCompleteness state;

        if (open.Any(e => e.State == PnlExpectationState.Overdue))
        {
            state = PnlLineCompleteness.Overdue;
        }
        else if (open.Any(e => e.State == PnlExpectationState.Expected))
        {
            state = PnlLineCompleteness.Provisional;
        }
        else if (all.Any(e => e.State == PnlExpectationState.ClosedWithException))
        {
            state = PnlLineCompleteness.ClosedWithException;
        }
        else
        {
            state = PnlLineCompleteness.Complete;
        }

        return new PnlLineCompletenessResult(state, missing, expectedBy);
    }
}

/// <summary>
/// Completeness of one line, as the projection stores it.
/// </summary>
/// <param name="State">Completeness state.</param>
/// <param name="MissingDocumentTypes">Entry types still expected, by name.</param>
/// <param name="ExpectedBy">Earliest deadline among the open expectations, or null.</param>
public sealed record PnlLineCompletenessResult(
    PnlLineCompleteness State,
    IReadOnlyList<string> MissingDocumentTypes,
    DateTimeOffset? ExpectedBy);
