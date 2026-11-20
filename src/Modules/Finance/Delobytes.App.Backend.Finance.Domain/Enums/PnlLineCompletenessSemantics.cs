namespace Delobytes.App.Backend.Finance.Domain.Enums;

/// <summary>
/// Classifies a completeness state by whether its figures may be reported as final. The read
/// filters and the line projection both use it, so a queried scope and the reported
/// <c>IsProvisional</c> flag cannot drift apart.
/// </summary>
public static class PnlLineCompletenessSemantics
{
    /// <summary>
    /// Returns whether the state means a document is still outstanding, so the figures may change.
    /// </summary>
    /// <param name="state">Completeness state.</param>
    /// <returns><c>true</c> for provisional and overdue lines.</returns>
    public static bool IsProvisional(PnlLineCompleteness state)
    {
        return state is PnlLineCompleteness.Provisional or PnlLineCompleteness.Overdue;
    }

    /// <summary>
    /// Returns whether the state means the figures are final: every document arrived, or the gap
    /// was accepted and recorded.
    /// </summary>
    /// <param name="state">Completeness state.</param>
    /// <returns><c>true</c> for complete and closed-with-exception lines.</returns>
    public static bool IsFinal(PnlLineCompleteness state)
    {
        return state is PnlLineCompleteness.Complete or PnlLineCompleteness.ClosedWithException;
    }
}
