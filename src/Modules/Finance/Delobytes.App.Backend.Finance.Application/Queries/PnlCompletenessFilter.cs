using Delobytes.App.Backend.Finance.Domain.Enums;

namespace Delobytes.App.Backend.Finance.Application.Queries;

/// <summary>
/// Reads the completeness filter of a read query. Shared by the line and the summary queries so
/// both apply the same default: an omitted filter means <c>complete</c>, because a report must not
/// blend a provisional figure into its totals.
/// </summary>
internal static class PnlCompletenessFilter
{
    /// <summary>
    /// Reads the requested completeness, defaulting to <see cref="PnlLineCompleteness.Complete"/>.
    /// </summary>
    /// <param name="raw">Raw value from the query string, or null.</param>
    /// <returns>The completeness to filter on.</returns>
    public static PnlLineCompleteness Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return PnlLineCompleteness.Complete;
        }

        return Enum.TryParse(raw, ignoreCase: true, out PnlLineCompleteness parsed)
            && Enum.IsDefined(parsed)
            ? parsed
            : PnlLineCompleteness.Complete;
    }
}
