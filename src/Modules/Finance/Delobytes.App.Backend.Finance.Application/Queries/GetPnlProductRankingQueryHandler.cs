using Delobytes.App.Backend.Catalog.Contracts.Lookup;
using Delobytes.App.Backend.Contracts.Accounting;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Finance.Application.Aggregation;
using Delobytes.App.Backend.Finance.Application.Interfaces;
using Delobytes.App.Backend.Finance.Application.Labeling;
using Delobytes.App.Backend.Finance.Application.Ranking;
using Delobytes.App.Backend.Finance.Application.ReadModels;
using MediatR;

namespace Delobytes.App.Backend.Finance.Application.Queries;

/// <summary>Response of the product ranking.</summary>
public sealed class PnlProductRankingResponse
{
    /// <summary>Gets or sets the ranked slice: at most <see cref="GetPnlProductRankingQuery.Take"/> rows.</summary>
    public List<PnlSummaryRowView> Rows { get; set; } = new();

    /// <summary>
    /// Gets or sets the number of products that carried the ranked metric and passed the filters.
    /// A client needs it to say "top 50 of 320", because the slice alone does not describe its
    /// population.
    /// </summary>
    public int RankedRows { get; set; }

    /// <summary>Gets or sets the number of product groups the period produced before ranking.</summary>
    public int TotalRows { get; set; }

    public string Currency { get; set; } = default!;

    public int ExcludedByCurrency { get; set; }

    public List<PnlCurrencyExclusion> ExcludedCurrencies { get; set; } = new();

    public PnlRankingSortKey SortBy { get; set; }

    public bool Descending { get; set; }

    public int Take { get; set; }

    public PnlRankingCoverage Coverage { get; set; }

    /// <summary>Gets or sets the revenue floor the ranking was restricted to, when one was set.</summary>
    public decimal? MinRevenue { get; set; }
}

/// <summary>
/// Ranking of products by one metric over the whole requested range. Unlike the summary this
/// request has no period or grouping axis: a ranking is always one row per product for one range,
/// which keeps its contract and its product rules narrower than the report's.
/// </summary>
public sealed class GetPnlProductRankingQuery : IRequest<PnlProductRankingResponse>
{
    public const int MaxTake = 200;

    public const int DefaultTake = 50;

    public PnlQueryFilter Filter { get; set; } = new();

    public PnlRankingSortKey SortBy { get; set; } = PnlRankingSortKey.Margin;

    public bool Descending { get; set; } = true;

    public int Take { get; set; } = DefaultTake;

    public PnlRankingCoverage Coverage { get; set; } = PnlRankingCoverage.Complete;

    public decimal? MinRevenue { get; set; }
}

/// <summary>
/// Handles <see cref="GetPnlProductRankingQuery"/>: the same fold and projection as the summary,
/// then a ranking slice and SKU labels for the rows that survive it.
/// </summary>
public sealed class GetPnlProductRankingQueryHandler
    : IRequestHandler<GetPnlProductRankingQuery, PnlProductRankingResponse>
{
    private readonly IPnlReadStore _store;
    private readonly ITenantContext _tenantContext;
    private readonly ITenantSettingsProvider _settingsProvider;
    private readonly IProductSkuLookup _skuLookup;

    public GetPnlProductRankingQueryHandler(
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
    public async Task<PnlProductRankingResponse> Handle(
        GetPnlProductRankingQuery request,
        CancellationToken cancellationToken)
    {
        Guid tenantId = _tenantContext.TenantId
            ?? throw new UnauthorizedAccessException("Пользователь не аутентифицирован.");

        TenantAccountingSettings settings =
            await _settingsProvider.GetAccountingSettingsAsync(tenantId, cancellationToken);

        PnlLineFilter filter = new()
        {
            ChannelId = request.Filter.ChannelId,
            ChannelProductId = request.Filter.ChannelProductId,
            From = request.Filter.From,
            To = request.Filter.To,
            CompletenessStates = PnlCompletenessFilter.Parse(request.Filter.Completeness),
        };

        PnlSummaryAggregationResult aggregation = await PnlSummaryAggregator.AggregateAsync(
            _store,
            _settingsProvider,
            tenantId,
            settings.Currency,
            filter,
            PnlSummaryGroupBy.Product,
            PnlPeriodGranularity.Total,
            null,
            cancellationToken);

        List<PnlSummaryRowView> rows = PnlSummaryProjector.BuildRows(
            aggregation.Groups,
            PnlSummaryGroupBy.Product,
            PnlPeriodGranularity.Total,
            filter.From,
            filter.To);

        PnlRankingResult ranking = PnlProductRanking.Rank(
            rows,
            request.SortBy,
            request.Descending,
            request.Take,
            request.Coverage,
            request.MinRevenue);

        // Labels are resolved after the slice: a product the ranking drops costs no lookup.
        await PnlSkuLabeler.ApplyAsync(_skuLookup, ranking.Rows, cancellationToken);

        return new PnlProductRankingResponse
        {
            Rows = ranking.Rows,
            RankedRows = ranking.RankedRows,
            TotalRows = ranking.TotalRows,
            Currency = settings.Currency,
            ExcludedByCurrency = aggregation.ExcludedCurrencies.Sum(exclusion => exclusion.Lines),
            ExcludedCurrencies = aggregation.ExcludedCurrencies,
            SortBy = request.SortBy,
            Descending = request.Descending,
            Take = request.Take,
            Coverage = request.Coverage,
            MinRevenue = request.MinRevenue,
        };
    }
}
