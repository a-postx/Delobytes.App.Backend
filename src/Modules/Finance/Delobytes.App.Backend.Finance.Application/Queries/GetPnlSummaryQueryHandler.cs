using Delobytes.App.Backend.Catalog.Contracts.Lookup;
using Delobytes.App.Backend.Contracts.Accounting;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Finance.Application.Aggregation;
using Delobytes.App.Backend.Finance.Application.Interfaces;
using Delobytes.App.Backend.Finance.Application.Labeling;
using Delobytes.App.Backend.Finance.Application.ReadModels;
using MediatR;

namespace Delobytes.App.Backend.Finance.Application.Queries;

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

/// <summary>
/// Handles <see cref="GetPnlSummaryQuery"/>: resolves the request, asks the aggregator for the
/// folded groups, projects them into rows and labels the rows with product SKUs.
/// </summary>
public sealed class GetPnlSummaryQueryHandler : IRequestHandler<GetPnlSummaryQuery, PnlSummaryResponse>
{
    // The row cap is a response budget, not an aggregation limit: at the current shape a row is
    // about 1.35 KB, so 7 000 rows keep the payload under the 10 MB budget. The ranking shares the
    // fold but not this budget, because it returns only its top slice.
    public const int MaxResponseRows = 7_000;

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
            ProductIds = request.Filter.ProductIds,
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
            request.GroupBy,
            request.Granularity,
            MaxResponseRows,
            cancellationToken);

        List<PnlSummaryRowView> rows = PnlSummaryProjector.BuildRows(
            aggregation.Groups,
            request.GroupBy,
            request.Granularity,
            filter.From,
            filter.To);

        await PnlSkuLabeler.ApplyAsync(_skuLookup, rows, cancellationToken);

        return new PnlSummaryResponse
        {
            Rows = rows,
            Currency = settings.Currency,
            GroupBy = request.GroupBy,
            ExcludedByCurrency = aggregation.ExcludedCurrencies.Sum(exclusion => exclusion.Lines),
            ExcludedCurrencies = aggregation.ExcludedCurrencies,
        };
    }
}
