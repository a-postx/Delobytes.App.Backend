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

    /// <summary>Reads every line the summary aggregates.</summary>
    public Task<IReadOnlyList<PnlLineRow>> GetForSummaryAsync(
        string currency,
        PnlLineFilter filter,
        CancellationToken cancellationToken);

    /// <summary>Reads the period diagnostics.</summary>
    public Task<PnlDiagnosticsResult> GetDiagnosticsAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken);
}
