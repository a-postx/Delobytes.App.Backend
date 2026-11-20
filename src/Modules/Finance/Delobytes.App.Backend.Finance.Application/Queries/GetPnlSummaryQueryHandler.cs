using Delobytes.App.Backend.Contracts.Accounting;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Finance.Application.Interfaces;
using Delobytes.App.Backend.Finance.Application.ReadModels;
using Delobytes.App.Backend.Finance.Domain.Enums;
using MediatR;

namespace Delobytes.App.Backend.Finance.Application.Queries;

/// <summary>One summary row.</summary>
public sealed class PnlSummaryRowView
{
    public Guid? ProductId { get; set; }

    public bool IsUnlinkedProduct { get; set; }

    public DateOnly PeriodStart { get; set; }

    public int Lines { get; set; }

    public decimal? RevenueAmount { get; set; }

    public decimal? NetRevenueAmount { get; set; }

    public decimal? ProductionCostAmount { get; set; }

    public decimal? EstimatedTax { get; set; }

    /// <summary>
    /// Gets or sets the margin before tax: the sum over the lines of the group whose cost is known
    /// and complete. Null when no line of the group has a known margin. A non-null value is still
    /// partial while <see cref="IsMarginComplete"/> is false, so compare it with
    /// <see cref="RevenueWithMarginAmount"/> rather than with <see cref="RevenueAmount"/>.
    /// </summary>
    public decimal? Margin { get; set; }

    /// <summary>
    /// Gets or sets the margin net of the estimated tax: the sum over the lines that have both a
    /// known margin and a tax profile. Null when no line of the group has both.
    /// </summary>
    public decimal? TaxAdjustedMargin { get; set; }

    /// <summary>
    /// Gets or sets the margin as a share of <see cref="RevenueWithMarginAmount"/>, that is, over
    /// the lines the margin itself covers. Null when the margin is unknown or the covered revenue
    /// is zero.
    /// </summary>
    public decimal? MarginPercent { get; set; }

    /// <summary>
    /// Gets or sets the return on the production cost of the covered lines only, never on
    /// <see cref="ProductionCostAmount"/>. Null when the margin is unknown or the covered cost is
    /// zero.
    /// </summary>
    public decimal? Roi { get; set; }

    /// <summary>Gets or sets the number of lines of the group whose margin is known.</summary>
    public int LinesWithMargin { get; set; }

    /// <summary>
    /// Gets or sets the revenue of the lines whose margin is known, on the same basis as
    /// <see cref="RevenueAmount"/>: the two coverage parts always add up to the total.
    /// </summary>
    public decimal RevenueWithMarginAmount { get; set; }

    /// <summary>Gets or sets the production cost of the lines whose margin is known.</summary>
    public decimal ProductionCostWithMarginAmount { get; set; }

    /// <summary>
    /// Gets or sets the revenue of the lines whose margin is unknown: no cost fact yet, an
    /// incomplete cost, or no net revenue. This is the part of <see cref="RevenueAmount"/> no
    /// margin figure can be attributed to.
    /// </summary>
    public decimal RevenueWithoutCostAmount { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether every line of the group has a known margin. Only
    /// then do <see cref="Margin"/>, <see cref="MarginPercent"/> and <see cref="Roi"/> describe the
    /// whole group.
    /// </summary>
    public bool IsMarginComplete { get; set; }

    public decimal? ControlDifferenceAmount { get; set; }

    /// <summary>
    /// Gets or sets the revenue of the group's provisional lines: the ones still waiting for a
    /// document or already past its window. It is the part of <see cref="RevenueAmount"/> whose
    /// figures are not final yet, so the UI can mark it without querying the states separately.
    /// Only meaningful when the query includes provisional lines.
    /// </summary>
    public decimal ProvisionalRevenueAmount { get; set; }

    /// <summary>
    /// Gets or sets the overdue part of <see cref="ProvisionalRevenueAmount"/>: the lines whose
    /// expected document is past its window and still open. It never exceeds the provisional amount.
    /// </summary>
    public decimal OverdueRevenueAmount { get; set; }

    /// <summary>
    /// Gets or sets the revenue of the lines closed with a recorded exception. These figures are
    /// final, but a document is missing behind them, so they stay distinguishable from a complete
    /// line.
    /// </summary>
    public decimal ClosedWithExceptionRevenueAmount { get; set; }

    /// <summary>
    /// Gets or sets the most severe completeness among the lines of the group: overdue beats
    /// provisional, provisional beats a recorded exception, and an exception beats a complete line.
    /// </summary>
    public PnlLineCompleteness CompletenessState { get; set; }
}

/// <summary>Response of the summary query.</summary>
public sealed class PnlSummaryResponse
{
    public List<PnlSummaryRowView> Rows { get; set; } = new();

    public string Currency { get; set; } = default!;
}

/// <summary>Summary by product and period.</summary>
public sealed class GetPnlSummaryQuery : IRequest<PnlSummaryResponse>
{
    public PnlQueryFilter Filter { get; set; } = new();

    public PnlPeriodGranularity Granularity { get; set; } = PnlPeriodGranularity.Day;
}

/// <summary>Handles <see cref="GetPnlSummaryQuery"/>.</summary>
public sealed class GetPnlSummaryQueryHandler : IRequestHandler<GetPnlSummaryQuery, PnlSummaryResponse>
{
    private readonly IPnlReadStore _store;
    private readonly ITenantContext _tenantContext;
    private readonly ITenantSettingsProvider _settingsProvider;

    public GetPnlSummaryQueryHandler(
        IPnlReadStore store,
        ITenantContext tenantContext,
        ITenantSettingsProvider settingsProvider)
    {
        _store = store;
        _tenantContext = tenantContext;
        _settingsProvider = settingsProvider;
    }

    /// <inheritdoc/>
    public async Task<PnlSummaryResponse> Handle(GetPnlSummaryQuery request, CancellationToken cancellationToken)
    {
        Guid tenantId = _tenantContext.TenantId
            ?? throw new UnauthorizedAccessException("Пользователь не аутентифицирован.");

        TenantAccountingSettings settings =
            await _settingsProvider.GetAccountingSettingsAsync(tenantId, cancellationToken);

        PnlLineFilter filter = new()
        {
            ChannelId = request.Filter.ChannelId,
            ProductId = request.Filter.ProductId,
            ChannelProductId = request.Filter.ChannelProductId,
            From = request.Filter.From,
            To = request.Filter.To,
            CompletenessStates = PnlCompletenessFilter.Parse(request.Filter.Completeness),
        };

        IReadOnlyList<PnlLineRow> rows = await _store.GetForSummaryAsync(settings.Currency, filter, cancellationToken);

        List<PnlLineView> views = await PnlViewFactory.BuildAsync(
            rows,
            tenantId,
            _settingsProvider,
            cancellationToken);

        // Indexed once instead of rescanning the whole page for every group: the old
        // `rows.Where(r => group.Any(...))` made the summary quadratic in the number of lines, which
        // is what actually times the request out on a deep history. One order line yields one P&L
        // line, so the identifier is a key.
        Dictionary<Guid, decimal?> controlDifferences = new(rows.Count);

        foreach (PnlLineRow row in rows)
        {
            controlDifferences[row.OrderLineId] = row.ControlDifferenceAmount;
        }

        // Grouped by the period date rather than by the sale date: a non-buyout never sold, but its
        // delivery charge is money of the period it was booked in and must be reported.
        IEnumerable<IGrouping<(Guid? ProductId, DateOnly PeriodStart), PnlLineView>> groups = views
            .Where(v => v.PeriodDate.HasValue)
            .GroupBy(v => (v.ProductId, PeriodStart: PeriodOf(v.PeriodDate!.Value, request.Granularity)));

        List<PnlSummaryRowView> summary = new();

        foreach (IGrouping<(Guid? ProductId, DateOnly PeriodStart), PnlLineView> group in groups)
        {
            summary.Add(BuildSummaryRow(group, controlDifferences));
        }

        return new PnlSummaryResponse
        {
            Rows = summary
                .OrderBy(r => r.PeriodStart)
                .ThenBy(r => r.ProductId)
                .ToList(),
            Currency = settings.Currency,
        };
    }

    // One pass over the lines of the group: every figure is accumulated where it belongs, so no
    // intermediate list per column is allocated and the coverage parts stay disjoint by construction.
    private static PnlSummaryRowView BuildSummaryRow(
        IGrouping<(Guid? ProductId, DateOnly PeriodStart), PnlLineView> group,
        IReadOnlyDictionary<Guid, decimal?> controlDifferences)
    {
        decimal? revenue = null;
        decimal? netRevenue = null;
        decimal? productionCost = null;
        decimal? estimatedTax = null;
        decimal? margin = null;
        decimal? taxAdjustedMargin = null;
        decimal? controlDifference = null;
        decimal revenueWithMargin = 0m;
        decimal productionCostWithMargin = 0m;
        decimal revenueWithoutCost = 0m;
        decimal provisionalRevenue = 0m;
        decimal overdueRevenue = 0m;
        decimal closedWithExceptionRevenue = 0m;
        int lines = 0;
        int linesWithMargin = 0;
        PnlLineCompleteness worst = PnlLineCompleteness.Complete;

        foreach (PnlLineView view in group)
        {
            lines++;

            revenue = Add(revenue, view.RevenueAmount);
            netRevenue = Add(netRevenue, view.NetRevenueAmount);
            productionCost = Add(productionCost, view.ProductionCostAmount);
            estimatedTax = Add(estimatedTax, view.EstimatedTax);
            taxAdjustedMargin = Add(taxAdjustedMargin, view.TaxAdjustedMargin);

            if (controlDifferences.TryGetValue(view.OrderLineId, out decimal? difference))
            {
                controlDifference = Add(controlDifference, difference);
            }

            // The margin exists only for a line with a known and complete cost, so it is summed over
            // its own population. Every figure derived from it is summed over the same population,
            // and the rest of the revenue is reported separately, so no percentage pairs a numerator
            // and a denominator that describe different lines.
            if (view.Margin.HasValue)
            {
                linesWithMargin++;
                margin = Add(margin, view.Margin);
                revenueWithMargin += view.RevenueAmount ?? 0m;
                productionCostWithMargin += view.ProductionCostAmount ?? 0m;
            }
            else
            {
                revenueWithoutCost += view.RevenueAmount ?? 0m;
            }

            // The provisional part is the same population the line view flags, so the badge and the
            // filter scopes cannot describe different lines.
            if (PnlLineCompletenessSemantics.IsProvisional(view.CompletenessState))
            {
                provisionalRevenue += view.RevenueAmount ?? 0m;
            }

            if (view.CompletenessState == PnlLineCompleteness.Overdue)
            {
                overdueRevenue += view.RevenueAmount ?? 0m;
            }

            if (view.CompletenessState == PnlLineCompleteness.ClosedWithException)
            {
                closedWithExceptionRevenue += view.RevenueAmount ?? 0m;
            }

            if (Severity(view.CompletenessState) > Severity(worst))
            {
                worst = view.CompletenessState;
            }
        }

        return new PnlSummaryRowView
        {
            ProductId = group.Key.ProductId,
            IsUnlinkedProduct = group.Key.ProductId == null,
            PeriodStart = group.Key.PeriodStart,
            Lines = lines,
            RevenueAmount = revenue,
            NetRevenueAmount = netRevenue,
            ProductionCostAmount = productionCost,
            EstimatedTax = estimatedTax,
            Margin = margin,
            TaxAdjustedMargin = taxAdjustedMargin,
            MarginPercent = PnlTaxCalculator.MarginPercent(margin, revenueWithMargin),
            Roi = PnlTaxCalculator.Roi(margin, productionCostWithMargin),
            LinesWithMargin = linesWithMargin,
            RevenueWithMarginAmount = revenueWithMargin,
            ProductionCostWithMarginAmount = productionCostWithMargin,
            RevenueWithoutCostAmount = revenueWithoutCost,
            IsMarginComplete = linesWithMargin == lines,
            ControlDifferenceAmount = controlDifference,
            ProvisionalRevenueAmount = provisionalRevenue,
            OverdueRevenueAmount = overdueRevenue,
            ClosedWithExceptionRevenueAmount = closedWithExceptionRevenue,
            CompletenessState = worst,
        };
    }

    private static DateOnly PeriodOf(DateOnly date, PnlPeriodGranularity granularity)
    {
        return granularity switch
        {
            PnlPeriodGranularity.Week => date.AddDays(-(((int)date.DayOfWeek + 6) % 7)),
            PnlPeriodGranularity.Month => new DateOnly(date.Year, date.Month, 1),
            _ => date,
        };
    }

    // The enum order is not an order of severity: a recorded exception is final, while an overdue
    // document is still missing, so the numeric value cannot be relied on.
    private static int Severity(PnlLineCompleteness state)
    {
        return state switch
        {
            PnlLineCompleteness.Overdue => 3,
            PnlLineCompleteness.Provisional => 2,
            PnlLineCompleteness.ClosedWithException => 1,
            _ => 0,
        };
    }

    // Sum over the values that are present, keeping the accumulator null while nothing is known:
    // an empty group of figures is unknown, not zero.
    private static decimal? Add(decimal? accumulator, decimal? value)
    {
        if (!value.HasValue)
        {
            return accumulator;
        }

        return accumulator.HasValue ? accumulator.Value + value.Value : value.Value;
    }
}
