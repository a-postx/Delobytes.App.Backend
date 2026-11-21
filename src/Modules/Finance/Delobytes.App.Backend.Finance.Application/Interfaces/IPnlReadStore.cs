using Delobytes.App.Backend.Finance.Application.ReadModels;

namespace Delobytes.App.Backend.Finance.Application.Interfaces;

/// <summary>
/// Read port of the P&amp;L model: lines with keyset paging, the rows a summary groups, and the
/// period diagnostics.
/// </summary>
public interface IPnlReadStore
{
    /// <summary>Reads one keyset page of P&amp;L lines in the requested order.</summary>
    /// <param name="currency">Reporting currency the page is read in.</param>
    /// <param name="filter">Filters of the page.</param>
    /// <param name="sort">Ordering of the page.</param>
    /// <param name="take">Maximum number of rows.</param>
    /// <param name="after">Cursor of the previous page, or null for the first page.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<PnlLinePage> GetLinesAsync(
        string currency,
        PnlLineFilter filter,
        PnlLineSort sort,
        int take,
        PnlLineCursor? after,
        CancellationToken cancellationToken);

    /// <summary>
    /// Streams every line the summary aggregates, one row at a time.
    /// </summary>
    /// <remarks>
    /// The result is not buffered: a report over a deep history reads its lines as a cursor and
    /// folds them into accumulators, so peak memory follows the number of groups rather than the
    /// number of lines. The rows come from a single statement, so they describe one snapshot of
    /// the data even while imports are running.
    /// </remarks>
    public IAsyncEnumerable<PnlLineRow> StreamForSummaryAsync(
        string currency,
        PnlLineFilter filter,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads the lines the report currency keeps out, grouped by their own currency.
    /// </summary>
    /// <remarks>
    /// The summary folds one currency at a time, so this is what makes the exclusion visible instead
    /// of silent. The amounts stay in the currency of the line: they are reported, not added up.
    /// </remarks>
    public Task<IReadOnlyList<PnlCurrencyExclusion>> GetCurrencyExclusionsAsync(
        string currency,
        PnlLineFilter filter,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads the diagnostics of one reporting currency: the journal entries of that currency, the
    /// lines and the expectations of the period. A channel filter narrows every figure to that
    /// channel, so the diagnostics describe the same population as a per-channel summary.
    /// </summary>
    /// <remarks>
    /// The diagnostics sit next to a single-currency summary, so they describe the same population.
    /// The lines of other currencies are reported as exclusions rather than counted into figures
    /// that a reader compares with the summary.
    /// </remarks>
    public Task<PnlDiagnosticsResult> GetDiagnosticsAsync(
        string currency,
        Guid? channelId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken);
}
