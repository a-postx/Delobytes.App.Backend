using Delobytes.App.Backend.Finance.Application.ReadModels;

namespace Delobytes.App.Backend.Finance.Application.Interfaces;

/// <summary>
/// Read port of the P&amp;L model: lines with keyset paging, the rows a summary groups, and the
/// period diagnostics.
/// </summary>
public interface IPnlReadStore
{
    /// <summary>Reads one keyset page of P&amp;L lines.</summary>
    public Task<PnlLinePage> GetLinesAsync(
        string currency,
        PnlLineFilter filter,
        int take,
        string? afterToken,
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

    /// <summary>Reads the period diagnostics.</summary>
    public Task<PnlDiagnosticsResult> GetDiagnosticsAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken);
}
