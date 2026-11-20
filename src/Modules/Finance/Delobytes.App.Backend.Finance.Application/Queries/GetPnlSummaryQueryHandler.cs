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

    /// <summary>
    /// Gets or sets the channel of the group. Populated only when the summary is grouped by channel;
    /// a product-grouped row leaves it empty and the channel stays a filter.
    /// </summary>
    public Guid? ChannelId { get; set; }

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

    /// <summary>
    /// Gets or sets the quantity of units the group was ordered with, including the non-buyouts: a
    /// line that never sold still carries the units it was ordered with.
    /// </summary>
    public int QuantityOrdered { get; set; }

    /// <summary>
    /// Gets or sets the quantity of units the group actually sold. A non-buyout adds to
    /// <see cref="QuantityOrdered"/> and to nothing here.
    /// </summary>
    public int QuantitySold { get; set; }

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

    /// <summary>
    /// Gets or sets the dimension the rows were grouped by. Echoed so a client never has to infer
    /// from the payload which identifier a row carries.
    /// </summary>
    public PnlSummaryGroupBy GroupBy { get; set; }
}

/// <summary>Summary by product or by channel, and by period.</summary>
public sealed class GetPnlSummaryQuery : IRequest<PnlSummaryResponse>
{
    public PnlQueryFilter Filter { get; set; } = new();

    public PnlPeriodGranularity Granularity { get; set; } = PnlPeriodGranularity.Day;

    /// <summary>
    /// Gets or sets the grouping dimension. One axis per request: a product-grouped row carries the
    /// product identifier, a channel-grouped row carries the channel one.
    /// </summary>
    public PnlSummaryGroupBy GroupBy { get; set; } = PnlSummaryGroupBy.Product;
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

        TaxProfileTimeline timeline = await PnlViewFactory.LoadTimelineAsync(
            tenantId,
            _settingsProvider,
            cancellationToken);

        // Grouped by the period date rather than by the sale date: a non-buyout never sold, but its
        // delivery charge is money of the period it was booked in and must be reported. Rows are
        // folded as they arrive, so the request never holds the whole history: peak memory follows
        // the number of groups (products or channels, times periods), not the number of lines.
        // The key carries the identifier of the requested dimension, so one fold serves both
        // groupings without a second pass over the lines.
        Dictionary<(Guid? GroupId, DateOnly PeriodStart), SummaryAccumulator> accumulators = new();

        await foreach (PnlLineRow row in _store
            .StreamForSummaryAsync(settings.Currency, filter, cancellationToken)
            .WithCancellation(cancellationToken))
        {
            PnlLineView view = PnlViewFactory.BuildRow(row, timeline);

            // A line without any date belongs to no period and is not reported.
            if (!view.PeriodDate.HasValue)
            {
                continue;
            }

            (Guid? GroupId, DateOnly PeriodStart) key = (
                request.GroupBy == PnlSummaryGroupBy.Channel ? view.ChannelId : view.ProductId,
                PeriodOf(view.PeriodDate.Value, request.Granularity));

            if (!accumulators.TryGetValue(key, out SummaryAccumulator? accumulator))
            {
                accumulator = new SummaryAccumulator();
                accumulators.Add(key, accumulator);
            }

            accumulator.Add(view, row.ControlDifferenceAmount);
        }

        return new PnlSummaryResponse
        {
            Rows = accumulators
                .Select(pair => pair.Value.Build(
                    request.GroupBy,
                    pair.Key.GroupId,
                    ResolvePeriodStart(request, pair.Value, pair.Key.PeriodStart)))
                .OrderBy(row => row.PeriodStart)
                .ThenBy(row => row.ProductId ?? row.ChannelId)
                .ToList(),
            Currency = settings.Currency,
            GroupBy = request.GroupBy,
        };
    }

    // A total-period row reports the start of the requested range. Without a range the earliest
    // period date of the group is the only honest answer: the grouping sentinel is an internal key
    // and must not leak into the payload as a real date.
    private static DateOnly ResolvePeriodStart(
        GetPnlSummaryQuery request,
        SummaryAccumulator accumulator,
        DateOnly groupedPeriodStart)
    {
        if (request.Granularity != PnlPeriodGranularity.Total)
        {
            return groupedPeriodStart;
        }

        return request.Filter.From
            ?? accumulator.EarliestPeriodDate
            ?? groupedPeriodStart;
    }

    // Folds line views into the totals of one group. Every figure is accumulated
    // where it belongs, so no intermediate list per column is allocated and the coverage parts stay
    // disjoint by construction.
    private sealed class SummaryAccumulator
    {
        private decimal? _revenue;
        private decimal? _netRevenue;
        private decimal? _productionCost;
        private decimal? _estimatedTax;
        private decimal? _margin;
        private decimal? _taxAdjustedMargin;
        private decimal? _controlDifference;
        private decimal _revenueWithMargin;
        private decimal _productionCostWithMargin;
        private decimal _revenueWithoutCost;
        private decimal _provisionalRevenue;
        private decimal _overdueRevenue;
        private decimal _closedWithExceptionRevenue;
        private int _lines;
        private int _linesWithMargin;
        private int _quantityOrdered;
        private int _quantitySold;
        private PnlLineCompleteness _worst = PnlLineCompleteness.Complete;

        /// <summary>
        /// Gets the earliest period date folded into the group. A total-period group is keyed by a
        /// sentinel, so this is what the reported period start falls back to.
        /// </summary>
        public DateOnly? EarliestPeriodDate { get; private set; }

        public void Add(PnlLineView view, decimal? controlDifference)
        {
            _lines++;

            _revenue = Accumulate(_revenue, view.RevenueAmount);
            _netRevenue = Accumulate(_netRevenue, view.NetRevenueAmount);
            _productionCost = Accumulate(_productionCost, view.ProductionCostAmount);
            _estimatedTax = Accumulate(_estimatedTax, view.EstimatedTax);
            _taxAdjustedMargin = Accumulate(_taxAdjustedMargin, view.TaxAdjustedMargin);
            _controlDifference = Accumulate(_controlDifference, controlDifference);

            // Both quantities are summed over every line of the group: the ordered units include a
            // non-buyout, the sold ones do not, and the two must describe the same population of
            // lines so a per-unit figure can be derived from either without a second scan.
            _quantityOrdered += view.QuantityOrdered;
            _quantitySold += view.QuantitySold;

            if (!EarliestPeriodDate.HasValue || view.PeriodDate < EarliestPeriodDate.Value)
            {
                EarliestPeriodDate = view.PeriodDate;
            }

            // The margin exists only for a line with a known and complete cost, so it is summed over
            // its own population. Every figure derived from it is summed over the same population,
            // and the rest of the revenue is reported separately, so no percentage pairs a numerator
            // and a denominator that describe different lines.
            if (view.Margin.HasValue)
            {
                _linesWithMargin++;
                _margin = Accumulate(_margin, view.Margin);
                _revenueWithMargin += view.RevenueAmount ?? 0m;
                _productionCostWithMargin += view.ProductionCostAmount ?? 0m;
            }
            else
            {
                _revenueWithoutCost += view.RevenueAmount ?? 0m;
            }

            // The provisional part is the same population the line view flags, so the badge and the
            // filter scopes cannot describe different lines.
            if (PnlLineCompletenessSemantics.IsProvisional(view.CompletenessState))
            {
                _provisionalRevenue += view.RevenueAmount ?? 0m;
            }

            if (view.CompletenessState == PnlLineCompleteness.Overdue)
            {
                _overdueRevenue += view.RevenueAmount ?? 0m;
            }

            if (view.CompletenessState == PnlLineCompleteness.ClosedWithException)
            {
                _closedWithExceptionRevenue += view.RevenueAmount ?? 0m;
            }

            if (Severity(view.CompletenessState) > Severity(_worst))
            {
                _worst = view.CompletenessState;
            }
        }

        public PnlSummaryRowView Build(
            PnlSummaryGroupBy groupBy,
            Guid? groupId,
            DateOnly periodStart)
        {
            bool byChannel = groupBy == PnlSummaryGroupBy.Channel;

            return new PnlSummaryRowView
            {
                // The dimension decides which identifier the row carries: a product row must not
                // expose a channel as if it were a product, and vice versa.
                ProductId = byChannel ? null : groupId,
                ChannelId = byChannel ? groupId : null,
                IsUnlinkedProduct = !byChannel && groupId == null,
                PeriodStart = periodStart,
                Lines = _lines,
                RevenueAmount = _revenue,
                NetRevenueAmount = _netRevenue,
                ProductionCostAmount = _productionCost,
                EstimatedTax = _estimatedTax,
                Margin = _margin,
                TaxAdjustedMargin = _taxAdjustedMargin,
                MarginPercent = PnlTaxCalculator.MarginPercent(_margin, _revenueWithMargin),
                Roi = PnlTaxCalculator.Roi(_margin, _productionCostWithMargin),
                QuantityOrdered = _quantityOrdered,
                QuantitySold = _quantitySold,
                LinesWithMargin = _linesWithMargin,
                RevenueWithMarginAmount = _revenueWithMargin,
                ProductionCostWithMarginAmount = _productionCostWithMargin,
                RevenueWithoutCostAmount = _revenueWithoutCost,
                IsMarginComplete = _linesWithMargin == _lines,
                ControlDifferenceAmount = _controlDifference,
                ProvisionalRevenueAmount = _provisionalRevenue,
                OverdueRevenueAmount = _overdueRevenue,
                ClosedWithExceptionRevenueAmount = _closedWithExceptionRevenue,
                CompletenessState = _worst,
            };
        }
    }

    private static DateOnly PeriodOf(DateOnly date, PnlPeriodGranularity granularity)
    {
        return granularity switch
        {
            // The whole range folds into one group. The start reported for it is resolved from the
            // request, so this sentinel stays an internal keying detail.
            PnlPeriodGranularity.Total => DateOnly.MinValue,
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
    private static decimal? Accumulate(decimal? accumulator, decimal? value)
    {
        if (!value.HasValue)
        {
            return accumulator;
        }

        return accumulator.HasValue ? accumulator.Value + value.Value : value.Value;
    }
}
