using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Finance.Application.Interfaces;
using Delobytes.App.Backend.Finance.Application.ReadModels;

namespace Delobytes.App.Backend.Tests.Application.Finance.Fakes;

/// <summary>
/// Read-store double for the P&amp;L handlers. Three tests used to carry their own copy: one returned
/// a page, one threw for the page and filtered the summary stream, one ignored the filter. The shared
/// fake returns the page and never filters unless the test asks for it, so the doubles cannot silently
/// disagree about what a filter means.
/// </summary>
internal sealed class PnlReadStoreFake : IPnlReadStore
{
    private readonly PnlLinePage _page;
    private readonly IReadOnlyList<PnlLineRow> _rows;

    public PnlReadStoreFake(PnlLinePage page, IReadOnlyList<PnlLineRow>? rows = null)
    {
        _page = page;
        _rows = rows ?? page.Rows;
    }

    public PnlReadStoreFake(IReadOnlyList<PnlLineRow> rows)
        : this(new PnlLinePage(), rows)
    {
    }

    /// <summary>
    /// Gets or sets a value indicating whether the summary stream applies the period itself. The
    /// store does it in production, so a test that reads a range must opt in; the default keeps the
    /// double transparent for handlers that already filter.
    /// </summary>
    public bool AppliesPeriodFilter { get; init; }

    public PnlLineFilter? LastFilter { get; private set; }

    /// <summary>Gets the ordering the handler asked for.</summary>
    public PnlLineSort? LastSort { get; private set; }

    /// <summary>Gets the cursor the handler parsed out of the continuation token.</summary>
    public PnlLineCursor? LastCursor { get; private set; }

    public PnlLineFilter? LastCurrencyFilter { get; private set; }

    /// <summary>Gets or sets the exclusions the summary handler reads next to the totals.</summary>
    public IReadOnlyList<PnlCurrencyExclusion> CurrencyExclusions { get; set; } =
        Array.Empty<PnlCurrencyExclusion>();

    /// <summary>Gets the number of times the summary stream was opened.</summary>
    public int StreamCount { get; private set; }

    /// <summary>Gets the number of rows the handler pulled from the stream.</summary>
    public int PulledRows { get; private set; }

    public Task<PnlLinePage> GetLinesAsync(
        string currency,
        PnlLineFilter filter,
        PnlLineSort sort,
        int take,
        PnlLineCursor? after,
        CancellationToken cancellationToken)
    {
        LastFilter = filter;
        LastSort = sort;
        LastCursor = after;

        return Task.FromResult(_page);
    }

    public Task<IReadOnlyList<PnlCurrencyExclusion>> GetCurrencyExclusionsAsync(
        string currency,
        PnlLineFilter filter,
        CancellationToken cancellationToken)
    {
        LastCurrencyFilter = filter;

        return Task.FromResult(CurrencyExclusions);
    }

    public async IAsyncEnumerable<PnlLineRow> StreamForSummaryAsync(
        string currency,
        PnlLineFilter filter,
        CancellationToken cancellationToken)
    {
        LastFilter = filter;
        StreamCount++;

        IEnumerable<PnlLineRow> selected = _rows;

        if (AppliesPeriodFilter)
        {
            if (filter.From.HasValue)
            {
                selected = SelectByPeriod(selected, row =>
                    row.PeriodDate.HasValue && row.PeriodDate.Value >= filter.From.Value);
            }

            if (filter.To.HasValue)
            {
                selected = SelectByPeriod(selected, row =>
                    row.PeriodDate.HasValue && row.PeriodDate.Value <= filter.To.Value);
            }
        }

        foreach (PnlLineRow row in selected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PulledRows++;
            yield return row;
        }

        await Task.CompletedTask;
    }

    public Task<PnlDiagnosticsResult> GetDiagnosticsAsync(
        string currency,
        Guid? channelId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(new PnlDiagnosticsResult());
    }

    private static IEnumerable<PnlLineRow> SelectByPeriod(
        IEnumerable<PnlLineRow> rows,
        Func<PnlLineRow, bool> predicate)
    {
        foreach (PnlLineRow row in rows)
        {
            if (predicate(row))
            {
                yield return row;
            }
        }
    }
}
