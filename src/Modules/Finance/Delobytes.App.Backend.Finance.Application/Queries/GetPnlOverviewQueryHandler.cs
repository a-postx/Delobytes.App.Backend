using Delobytes.App.Backend.Contracts.Accounting;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Finance.Application.Aggregation;
using Delobytes.App.Backend.Finance.Application.Interfaces;
using Delobytes.App.Backend.Finance.Application.ReadModels;
using MediatR;

namespace Delobytes.App.Backend.Finance.Application.Queries;

/// <summary>Metric of the overview strip.</summary>
public enum PnlOverviewKpiKey
{
    /// <summary>Gross revenue of the period.</summary>
    Revenue = 0,

    /// <summary>Revenue after every channel deduction.</summary>
    NetRevenue = 1,

    /// <summary>Units the period actually sold.</summary>
    Sold = 2,

    /// <summary>Revenue per sold unit.</summary>
    AverageCheck = 3,

    /// <summary>Share of the ordered units that was not returned, in percent.</summary>
    Buyout = 4,
}

/// <summary>Remark the overview raises about the period.</summary>
public enum PnlOverviewAttentionCode
{
    /// <summary>Expected documents past their window are still open.</summary>
    Overdue = 0,

    /// <summary>Sold lines without a known production cost: their margin is unknown.</summary>
    WithoutCost = 1,

    /// <summary>Revenue of lines whose catalogue product is not resolved.</summary>
    Unlinked = 2,

    /// <summary>Products whose known margin is negative.</summary>
    Loss = 3,

    /// <summary>Lines the reporting currency kept out of every figure.</summary>
    Currency = 4,
}

/// <summary>One metric with its movement against the comparison range.</summary>
public sealed class PnlOverviewKpiView
{
    public PnlOverviewKpiKey Key { get; set; }

    public decimal? Value { get; set; }

    public decimal? PreviousValue { get; set; }

    /// <summary>Gets or sets the relative change in percent; null when the comparison is meaningless.</summary>
    public decimal? DeltaPercent { get; set; }

    /// <summary>Gets or sets the absolute change in percentage points, for a metric that is already a share.</summary>
    public decimal? DeltaPoints { get; set; }
}

/// <summary>
/// Margin of the period with the figures a reader needs to judge it: the share of the revenue it
/// covers, the tax layer, and the movement against the comparison range.
/// </summary>
public sealed class PnlOverviewMarginView
{
    public decimal? Amount { get; set; }

    public decimal? PreviousAmount { get; set; }

    /// <summary>Gets or sets the change against the comparison range; null unless both sides are covered.</summary>
    public decimal? DeltaPercent { get; set; }

    public decimal? ContributionMarginPercent { get; set; }

    public decimal? ReturnOnSoldCostPercent { get; set; }

    /// <summary>Gets or sets the share of the revenue whose cost is known, in percent.</summary>
    public decimal? CoveragePercent { get; set; }

    public decimal? PreviousCoveragePercent { get; set; }

    /// <summary>Gets or sets a value indicating whether the margin covers every line of the period.</summary>
    public bool IsComplete { get; set; }

    /// <summary>Gets or sets a value indicating whether a tax profile exists for the period.</summary>
    public bool HasTaxProfile { get; set; }

    public decimal? EstimatedTaxAmount { get; set; }

    public decimal? TaxAdjustedMarginAmount { get; set; }

    /// <summary>Gets or sets a value indicating whether the tax layer covers every line of the period.</summary>
    public bool IsTaxAdjustedComplete { get; set; }
}

/// <summary>Channel of the period with its own figures and its share of the channel revenue.</summary>
public sealed class PnlOverviewChannelView
{
    public Guid? ChannelId { get; set; }

    public int Lines { get; set; }

    public decimal? RevenueAmount { get; set; }

    public decimal? NetRevenueAmount { get; set; }

    /// <summary>Gets or sets the money the channel withheld: revenue minus net revenue.</summary>
    public decimal? DeductionsAmount { get; set; }

    /// <summary>Gets or sets the withheld money as a percentage of the channel revenue.</summary>
    public decimal? DeductionsPercent { get; set; }

    public decimal? Margin { get; set; }

    public decimal? ContributionMarginPercent { get; set; }

    public bool IsMarginComplete { get; set; }

    /// <summary>Gets or sets the share of the revenue of the channels shown, in percent.</summary>
    public decimal SharePercent { get; set; }
}

/// <summary>A remark of the overview: what is wrong and how large the part of the period it holds is.</summary>
public sealed class PnlOverviewAttentionView
{
    public PnlOverviewAttentionCode Code { get; set; }

    /// <summary>Gets or sets the population the remark counts: lines, or products for a product remark.</summary>
    public int Count { get; set; }

    /// <summary>Gets or sets the money the remark describes, in the reporting currency.</summary>
    public decimal? Amount { get; set; }
}

/// <summary>Response of the overview query.</summary>
public sealed class PnlOverviewResponse
{
    public DateOnly From { get; set; }

    public DateOnly To { get; set; }

    public DateOnly? ComparisonFrom { get; set; }

    public DateOnly? ComparisonTo { get; set; }

    public string Currency { get; set; } = default!;

    /// <summary>
    /// Gets or sets the whole period folded into one row. Null when the period holds no lines, so a
    /// client reads an empty period as "no figures", not as zeroes.
    /// </summary>
    public PnlSummaryRowView? Totals { get; set; }

    public PnlSummaryRowView? PreviousTotals { get; set; }

    public List<PnlOverviewKpiView> Kpis { get; set; } = new();

    public PnlOverviewMarginView Margin { get; set; } = new();

    public List<PnlOverviewChannelView> Channels { get; set; } = new();

    public List<PnlOverviewAttentionView> Attention { get; set; } = new();

    /// <summary>
    /// Gets or sets the number of catalogue products the period holds. The rows themselves are not
    /// part of the overview: the page links to them instead of listing them.
    /// </summary>
    public int ProductCount { get; set; }

    public int ExcludedByCurrency { get; set; }

    public List<PnlCurrencyExclusion> ExcludedCurrencies { get; set; } = new();
}

/// <summary>
/// One request for the whole overview: the totals of the period, their movement against a
/// comparison range, the channels, and the remarks a reader should see before the figures.
/// </summary>
public sealed class GetPnlOverviewQuery : IRequest<PnlOverviewResponse>
{
    public PnlQueryFilter Filter { get; set; } = new();

    public DateOnly? ComparisonFrom { get; set; }

    public DateOnly? ComparisonTo { get; set; }
}

/// <summary>
/// Handles <see cref="GetPnlOverviewQuery"/>. The period is folded once along every dimension the
/// overview shows, so totals, channels and products cannot describe different snapshots of the
/// data; the comparison range is a second fold of its own.
/// </summary>
public sealed class GetPnlOverviewQueryHandler : IRequestHandler<GetPnlOverviewQuery, PnlOverviewResponse>
{
    // Below this share of the revenue a margin comparison pairs populations that differ too much
    // to be read as a change of the same figure.
    private const decimal MarginCoverageWarnPercent = 90m;

    private static readonly PnlSummaryGroupBy[] OverviewAxes =
    {
        PnlSummaryGroupBy.None,
        PnlSummaryGroupBy.Channel,
        PnlSummaryGroupBy.Product,
    };

    private readonly IPnlReadStore _store;
    private readonly ITenantContext _tenantContext;
    private readonly ITenantSettingsProvider _settingsProvider;

    public GetPnlOverviewQueryHandler(
        IPnlReadStore store,
        ITenantContext tenantContext,
        ITenantSettingsProvider settingsProvider)
    {
        _store = store;
        _tenantContext = tenantContext;
        _settingsProvider = settingsProvider;
    }

    /// <inheritdoc/>
    public async Task<PnlOverviewResponse> Handle(
        GetPnlOverviewQuery request,
        CancellationToken cancellationToken)
    {
        Guid tenantId = _tenantContext.TenantId
            ?? throw new UnauthorizedAccessException("Пользователь не аутентифицирован.");

        TenantAccountingSettings settings =
            await _settingsProvider.GetAccountingSettingsAsync(tenantId, cancellationToken);

        // The overview is the full picture of the period: its totals are the base the reader
        // reconciles with the source reports, so an omitted scope must not silently hide the
        // provisional part.
        PnlLineFilter filter = new()
        {
            ChannelId = request.Filter.ChannelId,
            ProductId = request.Filter.ProductId,
            ProductIds = request.Filter.ProductIds,
            ChannelProductId = request.Filter.ChannelProductId,
            From = request.Filter.From,
            To = request.Filter.To,
            CompletenessStates = PnlCompletenessFilter.Parse(
                request.Filter.Completeness ?? PnlCompletenessFilter.All),
        };

        // No response budget: the axes other than the totals are folded for their aggregates only,
        // so the number of rows never reaches the payload and the OOM ceiling stays the only limit.
        PnlSummaryAggregationSet current = await PnlSummaryAggregator.AggregateAsync(
            _store,
            _settingsProvider,
            tenantId,
            settings.Currency,
            filter,
            OverviewAxes,
            PnlPeriodGranularity.Total,
            maxGroups: null,
            cancellationToken);

        PnlSummaryRowView? totals = Project(current, PnlSummaryGroupBy.None, filter).FirstOrDefault();

        PnlSummaryRowView? previous = null;

        if (request.ComparisonFrom.HasValue && request.ComparisonTo.HasValue)
        {
            PnlLineFilter comparisonFilter = new()
            {
                ChannelId = filter.ChannelId,
                ProductId = filter.ProductId,
                ProductIds = filter.ProductIds,
                ChannelProductId = filter.ChannelProductId,
                From = request.ComparisonFrom,
                To = request.ComparisonTo,
                CompletenessStates = filter.CompletenessStates,
            };

            PnlSummaryAggregationSet comparison = await PnlSummaryAggregator.AggregateAsync(
                _store,
                _settingsProvider,
                tenantId,
                settings.Currency,
                comparisonFilter,
                new[] { PnlSummaryGroupBy.None },
                PnlPeriodGranularity.Total,
                maxGroups: null,
                cancellationToken);

            previous = Project(comparison, PnlSummaryGroupBy.None, comparisonFilter).FirstOrDefault();
        }

        List<PnlSummaryRowView> channels = Project(current, PnlSummaryGroupBy.Channel, filter);
        List<PnlSummaryRowView> products = Project(current, PnlSummaryGroupBy.Product, filter);

        return new PnlOverviewResponse
        {
            From = filter.From ?? totals?.PeriodStart ?? default,
            To = filter.To ?? totals?.PeriodEnd ?? default,
            ComparisonFrom = request.ComparisonFrom,
            ComparisonTo = request.ComparisonTo,
            Currency = settings.Currency,
            Totals = totals,
            PreviousTotals = previous,
            Kpis = BuildKpis(totals, previous),
            Margin = BuildMargin(totals, previous),
            Channels = BuildChannels(channels),
            Attention = BuildAttention(totals, products, current.ExcludedCurrencies),
            ProductCount = products.Count(row => row.ProductId.HasValue),
            ExcludedByCurrency = current.ExcludedCurrencies.Sum(exclusion => exclusion.Lines),
            ExcludedCurrencies = current.ExcludedCurrencies,
        };
    }

    private static List<PnlSummaryRowView> Project(
        PnlSummaryAggregationSet set,
        PnlSummaryGroupBy groupBy,
        PnlLineFilter filter)
    {
        return PnlSummaryProjector.BuildRows(
            set.Groups.TryGetValue(groupBy, out List<PnlSummaryGroup>? groups)
                ? groups
                : new List<PnlSummaryGroup>(),
            groupBy,
            PnlPeriodGranularity.Total,
            filter.From,
            filter.To);
    }

    private static List<PnlOverviewKpiView> BuildKpis(
        PnlSummaryRowView? totals,
        PnlSummaryRowView? previous)
    {
        decimal? averageCheck = AverageCheck(totals);
        decimal? buyout = BuyoutPercent(totals);

        return new List<PnlOverviewKpiView>
        {
            new()
            {
                Key = PnlOverviewKpiKey.Revenue,
                Value = totals?.RevenueAmount,
                PreviousValue = previous?.RevenueAmount,
                DeltaPercent = PercentDelta(totals?.RevenueAmount, previous?.RevenueAmount),
            },
            new()
            {
                Key = PnlOverviewKpiKey.NetRevenue,
                Value = totals?.NetRevenueAmount,
                PreviousValue = previous?.NetRevenueAmount,
                DeltaPercent = PercentDelta(totals?.NetRevenueAmount, previous?.NetRevenueAmount),
            },
            new()
            {
                Key = PnlOverviewKpiKey.Sold,
                Value = totals?.QuantitySold,
                PreviousValue = previous?.QuantitySold,
                DeltaPercent = PercentDelta(totals?.QuantitySold, previous?.QuantitySold),
            },
            new()
            {
                Key = PnlOverviewKpiKey.AverageCheck,
                Value = averageCheck,
                PreviousValue = AverageCheck(previous),
                DeltaPercent = PercentDelta(averageCheck, AverageCheck(previous)),
            },
            new()
            {
                Key = PnlOverviewKpiKey.Buyout,
                Value = buyout,
                PreviousValue = BuyoutPercent(previous),
                DeltaPoints = PointDelta(buyout, BuyoutPercent(previous)),
            },
        };
    }

    private static PnlOverviewMarginView BuildMargin(
        PnlSummaryRowView? totals,
        PnlSummaryRowView? previous)
    {
        decimal? coverage = CoveragePercent(totals);
        decimal? previousCoverage = CoveragePercent(previous);

        // A delta of a partially covered margin compares different populations, so it is reported
        // only when both sides are covered enough for the change to mean a change of the same figure.
        bool comparable = coverage >= MarginCoverageWarnPercent
            && previousCoverage >= MarginCoverageWarnPercent;

        return new PnlOverviewMarginView
        {
            Amount = totals?.Margin,
            PreviousAmount = previous?.Margin,
            DeltaPercent = comparable
                ? PercentDelta(totals?.Margin, previous?.Margin)
                : null,
            ContributionMarginPercent = totals?.ContributionMarginPercent,
            ReturnOnSoldCostPercent = totals?.ReturnOnSoldCostPercent,
            CoveragePercent = coverage,
            PreviousCoveragePercent = previousCoverage,
            IsComplete = totals?.IsMarginComplete ?? false,
            HasTaxProfile = totals?.EstimatedTax.HasValue == true,
            EstimatedTaxAmount = totals?.EstimatedTax,
            TaxAdjustedMarginAmount = totals?.TaxAdjustedMargin,
            IsTaxAdjustedComplete = totals?.IsTaxAdjustedMarginComplete ?? false,
        };
    }

    private static List<PnlOverviewChannelView> BuildChannels(List<PnlSummaryRowView> rows)
    {
        // A line without a channel is reported by the unlinked remark, not as a channel of its own.
        List<PnlSummaryRowView> withChannel = rows
            .Where(row => row.ChannelId.HasValue)
            .OrderByDescending(row => row.RevenueAmount ?? 0m)
            .ToList();

        decimal totalRevenue = withChannel.Sum(row => row.RevenueAmount ?? 0m);

        return withChannel
            .Select(row => new PnlOverviewChannelView
            {
                ChannelId = row.ChannelId,
                Lines = row.Lines,
                RevenueAmount = row.RevenueAmount,
                NetRevenueAmount = row.NetRevenueAmount,
                DeductionsAmount = row.RevenueAmount.HasValue && row.NetRevenueAmount.HasValue
                    ? row.RevenueAmount.Value - row.NetRevenueAmount.Value
                    : null,
                DeductionsPercent = row is { RevenueAmount: > 0m } && row.NetRevenueAmount.HasValue
                    ? Math.Round(
                        (row.RevenueAmount.Value - row.NetRevenueAmount.Value) / row.RevenueAmount.Value * 100m,
                        1)
                    : null,
                Margin = row.Margin,
                ContributionMarginPercent = row.ContributionMarginPercent,
                IsMarginComplete = row.IsMarginComplete,
                SharePercent = totalRevenue > 0m
                    ? Math.Round((row.RevenueAmount ?? 0m) / totalRevenue * 100m, 1)
                    : 0m,
            })
            .ToList();
    }

    private static List<PnlOverviewAttentionView> BuildAttention(
        PnlSummaryRowView? totals,
        List<PnlSummaryRowView> products,
        List<PnlCurrencyExclusion> excludedCurrencies)
    {
        List<PnlOverviewAttentionView> items = new();

        if (totals is { OverdueLines: > 0 })
        {
            items.Add(new PnlOverviewAttentionView
            {
                Code = PnlOverviewAttentionCode.Overdue,
                Count = totals.OverdueLines,
                Amount = totals.OverdueRevenueAmount,
            });
        }

        List<PnlSummaryRowView> withoutCost = products
            .Where(row => row.ProductId.HasValue && row.NetRevenueWithoutCostAmount > 0m)
            .ToList();

        if (withoutCost.Count > 0)
        {
            items.Add(new PnlOverviewAttentionView
            {
                Code = PnlOverviewAttentionCode.WithoutCost,
                Count = withoutCost.Count,
                Amount = withoutCost.Sum(row => row.NetRevenueWithoutCostAmount),
            });
        }

        List<PnlSummaryRowView> unlinked = products
            .Where(row => row.IsUnlinkedProduct && (row.RevenueAmount ?? 0m) > 0m)
            .ToList();

        if (unlinked.Count > 0)
        {
            items.Add(new PnlOverviewAttentionView
            {
                Code = PnlOverviewAttentionCode.Unlinked,
                Count = unlinked.Sum(row => row.Lines),
                Amount = unlinked.Sum(row => row.RevenueAmount ?? 0m),
            });
        }

        List<PnlSummaryRowView> loss = products
            .Where(row => row.Margin.HasValue && row.Margin.Value < 0m)
            .ToList();

        if (loss.Count > 0)
        {
            items.Add(new PnlOverviewAttentionView
            {
                Code = PnlOverviewAttentionCode.Loss,
                Count = loss.Count,
                Amount = loss.Sum(row => row.Margin ?? 0m),
            });
        }

        int excludedLines = excludedCurrencies.Sum(exclusion => exclusion.Lines);

        if (excludedLines > 0)
        {
            items.Add(new PnlOverviewAttentionView
            {
                Code = PnlOverviewAttentionCode.Currency,
                Count = excludedLines,
            });
        }

        return items;
    }

    private static decimal? AverageCheck(PnlSummaryRowView? row)
    {
        return row is { QuantitySold: > 0 } && row.RevenueAmount.HasValue
            ? row.RevenueAmount.Value / row.QuantitySold
            : null;
    }

    private static decimal? BuyoutPercent(PnlSummaryRowView? row)
    {
        return row is { QuantityOrdered: > 0 }
            ? (decimal)row.QuantitySold / row.QuantityOrdered * 100m
            : null;
    }

    private static decimal? CoveragePercent(PnlSummaryRowView? row)
    {
        return row is { RevenueAmount: > 0m }
            ? Math.Round(row.RevenueWithMarginAmount / row.RevenueAmount!.Value * 100m, 1)
            : null;
    }

    // A base of zero has no percentage change: the movement is unknown, not infinite.
    private static decimal? PercentDelta(decimal? current, decimal? previous)
    {
        if (!current.HasValue || !previous.HasValue || previous.Value == 0m)
        {
            return null;
        }

        return Math.Round((current.Value - previous.Value) / Math.Abs(previous.Value) * 100m, 1);
    }

    private static decimal? PointDelta(decimal? current, decimal? previous)
    {
        return current.HasValue && previous.HasValue
            ? Math.Round(current.Value - previous.Value, 1)
            : null;
    }
}
