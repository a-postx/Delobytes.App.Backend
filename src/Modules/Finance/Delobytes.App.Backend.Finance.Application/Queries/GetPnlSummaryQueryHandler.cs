using Delobytes.App.Backend.Catalog.Contracts.Lookup;
using Delobytes.App.Backend.Contracts.Accounting;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Finance.Application.Interfaces;
using Delobytes.App.Backend.Finance.Application.ReadModels;
using Delobytes.App.Backend.Finance.Domain.Enums;
using FluentValidation;
using FluentValidation.Results;
using MediatR;

namespace Delobytes.App.Backend.Finance.Application.Queries;

/// <summary>One summary row.</summary>
public sealed class PnlSummaryRowView
{
    public Guid? ProductId { get; set; }

    /// <summary>
    /// Gets or sets the current SKU of the linked catalogue product. Null when the row is not
    /// grouped by product, is unlinked, or the product no longer resolves.
    /// </summary>
    public string? Sku { get; set; }

    /// <summary>
    /// Gets or sets the channel of the group. Populated only when the summary is grouped by channel;
    /// a product-grouped row leaves it empty and the channel stays a filter.
    /// </summary>
    public Guid? ChannelId { get; set; }

    public bool IsUnlinkedProduct { get; set; }

    /// <summary>
    /// Gets or sets the first day of the period, unclipped: a week starts on its Monday and a month
    /// on its 1st even when the filter starts later, so the label always describes a whole period.
    /// </summary>
    public DateOnly PeriodStart { get; set; }

    /// <summary>
    /// Gets or sets the last day of the period, inclusive, on the same unclipped basis as
    /// <see cref="PeriodStart"/>. A total-period row reports the last day of the requested range.
    /// </summary>
    public DateOnly PeriodEnd { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the period is cut by the requested range: the row
    /// then holds only the days inside the filter while its label describes the whole period. A
    /// day period is never partial; a total-period row is the range itself and is never partial.
    /// </summary>
    public bool IsPartial { get; set; }

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
    /// Gets or sets the contribution margin net of the estimated tax, as a percentage of
    /// <see cref="NetRevenueWithTaxAdjustedMarginAmount"/>: 40 means 40%. The two describe the same
    /// lines, so they are never mixed. Null while the tax layer does not cover the whole group: a
    /// share of a partially taxed group would pair a numerator and a denominator that belong to
    /// different lines, which is not a conservative estimate but a wrong figure.
    /// </summary>
    public decimal? TaxAdjustedContributionMarginPercent { get; set; }

    /// <summary>
    /// Gets or sets the return on <see cref="InvestedCostWithTaxAdjustedMarginAmount"/> computed from
    /// <see cref="TaxAdjustedMargin"/>: 25 means 25% of the money tied up in the taxed lines whose
    /// investment is known. Null while that population is not the whole group, for the same reason
    /// as <see cref="TaxAdjustedContributionMarginPercent"/>.
    /// </summary>
    public decimal? TaxAdjustedRoiPercent { get; set; }

    /// <summary>
    /// Gets or sets the contribution margin as a percentage of <see cref="NetRevenueWithMarginAmount"/>:
    /// 40 means 40%. The margin is earned after the channel fees, so the gross sale is not its base.
    /// Null when the margin is unknown or the covered net revenue is not positive.
    /// </summary>
    public decimal? ContributionMarginPercent { get; set; }

    /// <summary>
    /// Gets or sets the return on the production cost of the covered sold units, as a percentage:
    /// 25 means 25% of <see cref="ProductionCostWithMarginAmount"/>, never of
    /// <see cref="ProductionCostAmount"/>. Null when the margin is unknown or the covered cost is
    /// not positive.
    /// </summary>
    public decimal? ReturnOnSoldCostPercent { get; set; }

    /// <summary>
    /// Gets or sets the return on <see cref="InvestedCostAmount"/>: 25 means 25% of the money tied up
    /// in the ordered units. The numerator is the margin of the same lines, so a non-buyout lowers
    /// the percentage instead of dropping out of the base. Null when the margin is unknown or the
    /// covered investment is not positive.
    /// </summary>
    public decimal? RoiPercent { get; set; }

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
    /// Gets or sets the number of lines of the group whose tax-adjusted margin is known: a known
    /// margin and a resolved tax profile. It is the population of <see cref="TaxAdjustedMargin"/>,
    /// so a percentage built on it never mixes lines.
    /// </summary>
    public int LinesWithTaxAdjustedMargin { get; set; }

    /// <summary>
    /// Gets or sets the revenue of the lines whose tax-adjusted margin is known, on the same basis as
    /// <see cref="RevenueAmount"/>: the tax base the tax layer actually covers. It never exceeds
    /// <see cref="RevenueWithMarginAmount"/>.
    /// </summary>
    public decimal RevenueWithTaxAdjustedMarginAmount { get; set; }

    /// <summary>
    /// Gets or sets the net revenue of the lines whose tax-adjusted margin is known: the base of
    /// <see cref="TaxAdjustedContributionMarginPercent"/>.
    /// </summary>
    public decimal NetRevenueWithTaxAdjustedMarginAmount { get; set; }

    /// <summary>
    /// Gets or sets the production cost of the ordered units of the lines whose tax-adjusted margin
    /// and investment are both known: the base of <see cref="TaxAdjustedRoiPercent"/>.
    /// </summary>
    public decimal InvestedCostWithTaxAdjustedMarginAmount { get; set; }

    /// <summary>Gets or sets the number of lines the tax-adjusted invested cost is known for.</summary>
    public int LinesWithTaxAdjustedInvestedCost { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether every line of the group has a known tax-adjusted
    /// margin. Only then does <see cref="TaxAdjustedContributionMarginPercent"/> describe the whole
    /// group, so the query reports the percentage only in that case.
    /// </summary>
    public bool IsTaxAdjustedMarginComplete { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether every line of the group has a known tax-adjusted
    /// margin and a known investment. Only then does <see cref="TaxAdjustedRoiPercent"/> describe the
    /// whole group.
    /// </summary>
    public bool IsTaxAdjustedRoiComplete { get; set; }

    /// <summary>
    /// Gets or sets the revenue of the lines whose margin is known, on the same basis as
    /// <see cref="RevenueAmount"/>: the two coverage parts always add up to the total.
    /// </summary>
    public decimal RevenueWithMarginAmount { get; set; }

    /// <summary>Gets or sets the production cost of the lines whose margin is known.</summary>
    public decimal ProductionCostWithMarginAmount { get; set; }

    /// <summary>
    /// Gets or sets the net revenue of the lines whose margin is known: the base of
    /// <see cref="ContributionMarginPercent"/>. On the same basis as <see cref="NetRevenueAmount"/>,
    /// so the two coverage parts always add up to the total.
    /// </summary>
    public decimal NetRevenueWithMarginAmount { get; set; }

    /// <summary>
    /// Gets or sets the net revenue of the lines whose margin is unknown: the part of
    /// <see cref="NetRevenueAmount"/> no margin figure can be attributed to.
    /// </summary>
    public decimal NetRevenueWithoutCostAmount { get; set; }

    /// <summary>
    /// Gets or sets the production cost of the ordered units of the lines whose margin and cost are
    /// both known: the base of <see cref="RoiPercent"/>. It covers the units that never sold, so it
    /// can exceed <see cref="ProductionCostWithMarginAmount"/>.
    /// </summary>
    public decimal InvestedCostAmount { get; set; }

    /// <summary>Gets or sets the number of lines the invested cost is known for.</summary>
    public int LinesWithInvestedCost { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether every line of the group has a known invested cost.
    /// Only then does <see cref="RoiPercent"/> describe the whole group.
    /// </summary>
    public bool IsRoiComplete { get; set; }

    /// <summary>
    /// Gets or sets the revenue of the lines whose margin is unknown: no cost fact yet, an
    /// incomplete cost, or no net revenue. This is the part of <see cref="RevenueAmount"/> no
    /// margin figure can be attributed to.
    /// </summary>
    public decimal RevenueWithoutCostAmount { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether every line of the group has a known margin. Only
    /// then do <see cref="Margin"/>, <see cref="ContributionMarginPercent"/> and
    /// <see cref="ReturnOnSoldCostPercent"/> describe the whole group.
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

    /// <summary>
    /// Gets or sets the number of lines the report currency kept out. The rows describe one currency
    /// only, so a non-zero value means the period holds money no row of this response shows.
    /// </summary>
    public int ExcludedByCurrency { get; set; }

    /// <summary>
    /// Gets or sets the excluded lines grouped by their currency, in their own currency: amounts of
    /// different currencies are never converted or added up here. Empty means the report is complete
    /// for the requested filters.
    /// </summary>
    public List<PnlCurrencyExclusion> ExcludedCurrencies { get; set; } = new();
}

/// <summary>Summary by product, by channel, or across the whole range, and by period.</summary>
public sealed class GetPnlSummaryQuery : IRequest<PnlSummaryResponse>
{
    public PnlQueryFilter Filter { get; set; } = new();

    public PnlPeriodGranularity Granularity { get; set; } = PnlPeriodGranularity.Day;

    /// <summary>
    /// Gets or sets the grouping dimension. One axis per request: a product-grouped row carries the
    /// product identifier, a channel-grouped row carries the channel one, and a dimensionless row
    /// carries neither.
    /// </summary>
    public PnlSummaryGroupBy GroupBy { get; set; } = PnlSummaryGroupBy.Product;
}

/// <summary>Handles <see cref="GetPnlSummaryQuery"/>.</summary>
public sealed class GetPnlSummaryQueryHandler : IRequestHandler<GetPnlSummaryQuery, PnlSummaryResponse>
{
    // The period axis is bounded by PnlPeriodBuckets.MaxPeriods, but the other axis is the number of
    // groups, which no date range bounds. The ceiling is set far above a real report: it turns an
    // out-of-memory response into a rejected request.
    private const int MaxSummaryRows = 200_000;

    private readonly IPnlReadStore _store;
    private readonly ITenantContext _tenantContext;
    private readonly ITenantSettingsProvider _settingsProvider;
    private readonly IProductSkuLookup _skuLookup;

    public GetPnlSummaryQueryHandler(
        IPnlReadStore store,
        ITenantContext tenantContext,
        ITenantSettingsProvider settingsProvider,
        IProductSkuLookup skuLookup)
    {
        _store = store;
        _tenantContext = tenantContext;
        _settingsProvider = settingsProvider;
        _skuLookup = skuLookup;
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

            // The key carries the identifier of the requested dimension, so one fold serves all
            // three groupings without a second pass over the lines. A dimensionless group has no
            // identifier at all: the period alone keys it.
            (Guid? GroupId, DateOnly PeriodStart) key = (
                request.GroupBy switch
                {
                    PnlSummaryGroupBy.Channel => view.ChannelId,
                    PnlSummaryGroupBy.None => null,
                    _ => view.ProductId,
                },
                PnlPeriodBuckets.StartOf(view.PeriodDate.Value, request.Granularity));

            if (!accumulators.TryGetValue(key, out SummaryAccumulator? accumulator))
            {
                if (accumulators.Count >= MaxSummaryRows)
                {
                    throw new ValidationException(new[]
                    {
                        new ValidationFailure(
                            nameof(GetPnlSummaryQuery.Filter),
                            $"Сводка получилась слишком большой: сузьте диапазон, гранулярность или фильтры. Строк больше {MaxSummaryRows}."),
                    });
                }

                accumulator = new SummaryAccumulator();
                accumulators.Add(key, accumulator);
            }

            accumulator.Add(view, row.ControlDifferenceAmount);
        }

        // The validator measures a closed range before the query runs. An open range takes one of its
        // edges from the data, so the same ceiling is enforced here, after the fold.
        EnsurePeriodLimit(accumulators, request);

        // A dimensionless trend is one series the client plots as-is, so its periods must be
        // contiguous. A grouped summary stays sparse: a product without lines in a period is not a
        // zero, and a synthetic row cannot name the groups that never appeared.
        FillEmptyPeriods(accumulators, request);

        // The fold reads one currency only. The money of the other currencies is reported next to the
        // totals rather than dropped: a report that silently loses revenue cannot be reconciled.
        IReadOnlyList<PnlCurrencyExclusion> excludedCurrencies = await _store.GetCurrencyExclusionsAsync(
            settings.Currency,
            filter,
            cancellationToken);

        List<PnlSummaryRowView> rows = accumulators
            .Select(pair =>
            {
                DateOnly periodStart = ResolvePeriodStart(request, pair.Value, pair.Key.PeriodStart);
                DateOnly periodEnd = ResolvePeriodEnd(request, pair.Value, periodStart);

                return pair.Value.Build(
                    request.GroupBy,
                    pair.Key.GroupId,
                    periodStart,
                    periodEnd,
                    IsPartial(request, periodStart, periodEnd));
            })
            .OrderBy(row => row.PeriodStart)
            .ThenBy(row => row.ProductId ?? row.ChannelId)
            .ToList();

        await EnrichSkusAsync(rows, request.GroupBy, cancellationToken);

        return new PnlSummaryResponse
        {
            Rows = rows,
            Currency = settings.Currency,
            GroupBy = request.GroupBy,
            ExcludedByCurrency = excludedCurrencies.Sum(exclusion => exclusion.Lines),
            ExcludedCurrencies = excludedCurrencies.ToList(),
        };
    }

    // Read after the final row list is built, so a later top-N limit never pays for the SKU of a
    // product it drops. A channel or dimensionless summary has no product axis to label.
    private async Task EnrichSkusAsync(
        List<PnlSummaryRowView> rows,
        PnlSummaryGroupBy groupBy,
        CancellationToken cancellationToken)
    {
        if (groupBy != PnlSummaryGroupBy.Product)
        {
            return;
        }

        Guid[] productIds = rows
            .Where(row => row.ProductId.HasValue)
            .Select(row => row.ProductId!.Value)
            .Distinct()
            .ToArray();

        if (productIds.Length == 0)
        {
            return;
        }

        IReadOnlyDictionary<Guid, string> skus = await _skuLookup.GetSkusAsync(productIds, cancellationToken);

        foreach (PnlSummaryRowView row in rows)
        {
            if (row.ProductId.HasValue && skus.TryGetValue(row.ProductId.Value, out string sku))
            {
                row.Sku = sku;
            }
        }
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

    // A total-period row covers the requested range, so its end is the end of the filter and falls
    // back to the latest period date folded into the group when the range is open.
    private static DateOnly ResolvePeriodEnd(
        GetPnlSummaryQuery request,
        SummaryAccumulator accumulator,
        DateOnly periodStart)
    {
        if (request.Granularity == PnlPeriodGranularity.Total)
        {
            return request.Filter.To
                ?? accumulator.LatestPeriodDate
                ?? periodStart;
        }

        return PnlPeriodBuckets.EndOf(periodStart, request.Granularity);
    }

    // A period is partial only when a filter bound cuts it: the days outside the range are missing
    // while the label still describes the whole period. A day period is atomic, and a total-period
    // row is the range itself.
    private static bool IsPartial(GetPnlSummaryQuery request, DateOnly periodStart, DateOnly periodEnd)
    {
        if (request.Granularity == PnlPeriodGranularity.Total)
        {
            return false;
        }

        return (request.Filter.From.HasValue && periodStart < request.Filter.From.Value)
            || (request.Filter.To.HasValue && periodEnd > request.Filter.To.Value);
    }

    // Adds a zero row for every period of the dimensionless series that has no lines, so a chart
    // drawn from the payload has no silent holes. Total is skipped because its single group is keyed
    // by a sentinel that must not surface as a real date.
    private static void FillEmptyPeriods(
        Dictionary<(Guid? GroupId, DateOnly PeriodStart), SummaryAccumulator> accumulators,
        GetPnlSummaryQuery request)
    {
        if (request.GroupBy != PnlSummaryGroupBy.None
            || request.Granularity == PnlPeriodGranularity.Total)
        {
            return;
        }

        if (!TryResolveGrid(request, accumulators, out DateOnly firstBucket, out DateOnly lastBucket))
        {
            return;
        }

        (Guid? GroupId, DateOnly PeriodStart) key = (null, firstBucket);

        while (key.PeriodStart <= lastBucket)
        {
            accumulators.TryAdd(key, SummaryAccumulator.EmptyPeriod());

            // The cap is checked against the resolved grid, not here: an edge may come from the last
            // line, and the next period after the last one may overflow DateOnly.
            if (key.PeriodStart == lastBucket)
            {
                break;
            }

            key = (null, PnlPeriodBuckets.Next(key.PeriodStart, request.Granularity));
        }
    }

    // A closed range is measured by the validator before the query runs. With an open bound one edge
    // is only known after the fold, so the same ceiling is applied here; the handler is also the
    // backstop for a caller that sends the request without the pipeline.
    private static void EnsurePeriodLimit(
        Dictionary<(Guid? GroupId, DateOnly PeriodStart), SummaryAccumulator> accumulators,
        GetPnlSummaryQuery request)
    {
        if (request.Granularity == PnlPeriodGranularity.Total)
        {
            return;
        }

        if (!TryResolveGrid(request, accumulators, out DateOnly firstBucket, out DateOnly lastBucket))
        {
            return;
        }

        if (PnlPeriodBuckets.Count(firstBucket, lastBucket, request.Granularity) <= PnlPeriodBuckets.MaxPeriods)
        {
            return;
        }

        throw new ValidationException(new[]
        {
            new ValidationFailure(
                nameof(GetPnlSummaryQuery.Filter),
                $"Диапазон не может превышать {PnlPeriodBuckets.MaxPeriods} периодов выбранной гранулярности."),
        });
    }

    // The grid is the requested range when a bound is given and the observed span otherwise: history
    // has no known edges to extend to.
    private static bool TryResolveGrid(
        GetPnlSummaryQuery request,
        Dictionary<(Guid? GroupId, DateOnly PeriodStart), SummaryAccumulator> accumulators,
        out DateOnly firstBucket,
        out DateOnly lastBucket)
    {
        if (request.Filter.From.HasValue)
        {
            firstBucket = PnlPeriodBuckets.StartOf(request.Filter.From.Value, request.Granularity);
        }
        else if (accumulators.Count > 0)
        {
            firstBucket = accumulators.Keys.Min(key => key.PeriodStart);
        }
        else
        {
            firstBucket = default;
            lastBucket = default;
            return false;
        }

        if (request.Filter.To.HasValue)
        {
            lastBucket = PnlPeriodBuckets.StartOf(request.Filter.To.Value, request.Granularity);
        }
        else if (accumulators.Count > 0)
        {
            lastBucket = accumulators.Keys.Max(key => key.PeriodStart);
        }
        else
        {
            lastBucket = default;
            return false;
        }

        return true;
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
        private decimal _netRevenueWithMargin;
        private decimal _netRevenueWithoutCost;
        private decimal _taxAdjustedRevenue;
        private decimal _taxAdjustedNetRevenue;
        private decimal? _marginWithInvestedCost;
        private decimal _investedCost;
        private decimal _taxAdjustedInvestedCost;
        private int _linesWithInvestedCost;
        private decimal _provisionalRevenue;
        private decimal _overdueRevenue;
        private decimal _closedWithExceptionRevenue;
        private int _lines;
        private int _linesWithMargin;
        private int _linesWithTaxAdjustedMargin;
        private int _linesWithTaxAdjustedInvestedCost;
        private int _quantityOrdered;
        private int _quantitySold;
        private PnlLineCompleteness _worst = PnlLineCompleteness.Complete;

        /// <summary>
        /// Gets the earliest period date folded into the group. A total-period group is keyed by a
        /// sentinel, so this is what the reported period start falls back to.
        /// </summary>
        public DateOnly? EarliestPeriodDate { get; private set; }

        /// <summary>
        /// Gets the latest period date folded into the group. A total-period group reports it as the
        /// period end when the filter has no upper bound.
        /// </summary>
        public DateOnly? LatestPeriodDate { get; private set; }

        // A period without lines: the money it would have carried is zero, not unknown, so the
        // fields accumulate from zero. The margin stays null, as no line has a cost to derive it.
        public static SummaryAccumulator EmptyPeriod()
        {
            return new SummaryAccumulator
            {
                _revenue = 0m,
                _netRevenue = 0m,
                _productionCost = 0m,
                _estimatedTax = 0m,
                _controlDifference = 0m,
                _netRevenueWithMargin = 0m,
                _netRevenueWithoutCost = 0m,
                _investedCost = 0m,
            };
        }

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

            if (!LatestPeriodDate.HasValue || view.PeriodDate > LatestPeriodDate.Value)
            {
                LatestPeriodDate = view.PeriodDate;
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
                _netRevenueWithMargin += view.NetRevenueAmount ?? 0m;
            }
            else
            {
                _revenueWithoutCost += view.RevenueAmount ?? 0m;
                _netRevenueWithoutCost += view.NetRevenueAmount ?? 0m;
            }

            // The ROI population is an intersection: a margin to show a return on and a known cost of
            // the ordered units to divide it by. Its numerator is kept apart from the full margin, so
            // the percentage never pairs a numerator and a denominator that describe different lines.
            if (view.Margin.HasValue && view.InvestedCostAmount.HasValue)
            {
                _marginWithInvestedCost = Accumulate(_marginWithInvestedCost, view.Margin);
                _investedCost += view.InvestedCostAmount.Value;
                _linesWithInvestedCost++;
            }

            // The tax layer has its own population, narrower than the margin one: a line needs both a
            // known margin and a resolved profile. Every tax-adjusted figure is accumulated here, so
            // its percentage pairs the same lines as its own denominator.
            if (view.TaxAdjustedMargin.HasValue)
            {
                _linesWithTaxAdjustedMargin++;
                _taxAdjustedRevenue += view.RevenueAmount ?? 0m;
                _taxAdjustedNetRevenue += view.NetRevenueAmount ?? 0m;

                if (view.InvestedCostAmount.HasValue)
                {
                    _taxAdjustedInvestedCost += view.InvestedCostAmount.Value;
                    _linesWithTaxAdjustedInvestedCost++;
                }
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
            DateOnly periodStart,
            DateOnly periodEnd,
            bool isPartial)
        {
            bool byChannel = groupBy == PnlSummaryGroupBy.Channel;
            bool dimensionless = groupBy == PnlSummaryGroupBy.None;

            // A percentage is reported only when its own population is the whole group: otherwise the
            // figure would be correct for some lines and silently read as if it described all of them.
            bool taxMarginComplete = _lines > 0 && _linesWithTaxAdjustedMargin == _lines;
            bool taxRoiComplete = _lines > 0 && _linesWithTaxAdjustedInvestedCost == _lines;

            return new PnlSummaryRowView
            {
                // The dimension decides which identifier the row carries: a product row must not
                // expose a channel as if it were a product, and vice versa. A dimensionless row
                // carries neither, and an empty identifier there is not an unresolved product.
                ProductId = !byChannel && !dimensionless ? groupId : null,
                ChannelId = byChannel ? groupId : null,
                IsUnlinkedProduct = !byChannel && !dimensionless && groupId == null,
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                IsPartial = isPartial,
                Lines = _lines,
                RevenueAmount = _revenue,
                NetRevenueAmount = _netRevenue,
                ProductionCostAmount = _productionCost,
                EstimatedTax = _estimatedTax,
                Margin = _margin,
                TaxAdjustedMargin = _taxAdjustedMargin,
                ContributionMarginPercent =
                    PnlTaxCalculator.ContributionMarginPercent(_margin, _netRevenueWithMargin),
                TaxAdjustedContributionMarginPercent = taxMarginComplete
                    ? PnlTaxCalculator.ContributionMarginPercent(_taxAdjustedMargin, _taxAdjustedNetRevenue)
                    : null,
                ReturnOnSoldCostPercent =
                    PnlTaxCalculator.ReturnOnSoldCostPercent(_margin, _productionCostWithMargin),
                RoiPercent = PnlTaxCalculator.RoiPercent(_marginWithInvestedCost, _investedCost),
                TaxAdjustedRoiPercent = taxRoiComplete
                    ? PnlTaxCalculator.RoiPercent(_taxAdjustedMargin, _taxAdjustedInvestedCost)
                    : null,
                QuantityOrdered = _quantityOrdered,
                QuantitySold = _quantitySold,
                LinesWithMargin = _linesWithMargin,
                LinesWithTaxAdjustedMargin = _linesWithTaxAdjustedMargin,
                RevenueWithMarginAmount = _revenueWithMargin,
                ProductionCostWithMarginAmount = _productionCostWithMargin,
                RevenueWithoutCostAmount = _revenueWithoutCost,
                NetRevenueWithMarginAmount = _netRevenueWithMargin,
                NetRevenueWithoutCostAmount = _netRevenueWithoutCost,
                RevenueWithTaxAdjustedMarginAmount = _taxAdjustedRevenue,
                NetRevenueWithTaxAdjustedMarginAmount = _taxAdjustedNetRevenue,
                InvestedCostAmount = _investedCost,
                LinesWithInvestedCost = _linesWithInvestedCost,
                InvestedCostWithTaxAdjustedMarginAmount = _taxAdjustedInvestedCost,
                LinesWithTaxAdjustedInvestedCost = _linesWithTaxAdjustedInvestedCost,
                // A group with no lines at all has no margin to be complete about, so an empty
                // period must not report itself as fully covered.
                IsMarginComplete = _lines > 0 && _linesWithMargin == _lines,
                IsTaxAdjustedMarginComplete = taxMarginComplete,
                // A line without any cost leaves the investment unknown, so a group is ROI-complete
                // only when its cost fact is known for every line.
                IsRoiComplete = _lines > 0 && _linesWithInvestedCost == _lines,
                IsTaxAdjustedRoiComplete = taxRoiComplete,
                ControlDifferenceAmount = _controlDifference,
                ProvisionalRevenueAmount = _provisionalRevenue,
                OverdueRevenueAmount = _overdueRevenue,
                ClosedWithExceptionRevenueAmount = _closedWithExceptionRevenue,
                CompletenessState = _worst,
            };
        }
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
