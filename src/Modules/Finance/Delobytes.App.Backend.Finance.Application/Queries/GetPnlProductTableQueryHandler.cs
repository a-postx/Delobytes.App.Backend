using Delobytes.App.Backend.Catalog.Contracts.Lookup;
using Delobytes.App.Backend.Contracts.Accounting;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Finance.Application.Aggregation;
using Delobytes.App.Backend.Finance.Application.Interfaces;
using Delobytes.App.Backend.Finance.Application.Labeling;
using Delobytes.App.Backend.Finance.Application.ProductTable;
using Delobytes.App.Backend.Finance.Application.Queries;
using Delobytes.App.Backend.Finance.Application.ReadModels;
using MediatR;

namespace Delobytes.App.Backend.Finance.Application.Queries;

/// <summary>Paged product table of a period.</summary>
public sealed class PnlProductTableResponse
{
    public List<PnlProductRow> Items { get; set; } = new();

    public int TotalCount { get; set; }

    public int Page { get; set; }

    public int PageSize { get; set; }

    public PnlProductSegment Segment { get; set; }

    /// <summary>Gets or sets the normalized search the items were filtered by; null when none.</summary>
    public string? Search { get; set; }

    public PnlProductSortKey SortBy { get; set; }

    public bool Descending { get; set; }

    /// <summary>Gets or sets the product counts after the search, before the segment filter.</summary>
    public PnlProductSegmentCounts Counts { get; set; } = new();

    /// <summary>Gets or sets the figures over the whole population, independent of search and paging.</summary>
    public PnlProductSummary Summary { get; set; } = new();

    /// <summary>Gets or sets the aggregate of sales without a catalogue link; null when there are none.</summary>
    public PnlUnlinkedSales? Unlinked { get; set; }

    public string Currency { get; set; } = default!;

    public int ExcludedByCurrency { get; set; }

    public List<PnlCurrencyExclusion> ExcludedCurrencies { get; set; } = new();
}

/// <summary>Counts the segment filter needs to describe its own options.</summary>
public sealed class PnlProductSegmentCounts
{
    public int All { get; set; }

    public int Loss { get; set; }

    public int WithoutCost { get; set; }
}

/// <summary>KPI strip of the page: one figure per cell, already over the whole population.</summary>
public sealed class PnlProductSummary
{
    public int Products { get; set; }

    public int LossProducts { get; set; }

    public decimal LossMarginAmount { get; set; }

    public int WithoutCostProducts { get; set; }

    public decimal WithoutCostNetRevenueAmount { get; set; }

    public decimal? MedianContributionMarginPercent { get; set; }
}

/// <summary>Sales without a catalogue link. Not a product, so it stays out of every product figure.</summary>
public sealed class PnlUnlinkedSales
{
    public int Lines { get; set; }

    public decimal? RevenueAmount { get; set; }

    public decimal? NetRevenueAmount { get; set; }
}

/// <summary>
/// One page of the product table: the products of the range with their economy, filtered, ordered
/// and sliced by the server so the client only prints what it receives.
/// </summary>
public sealed class GetPnlProductTableQuery : IRequest<PnlProductTableResponse>
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 200;
    public const int MaxSearchLength = 200;

    public PnlQueryFilter Filter { get; set; } = new();

    public PnlProductSegment Segment { get; set; } = PnlProductSegment.All;

    public string? Search { get; set; }

    public PnlProductSortKey SortBy { get; set; } = PnlProductSortKey.Margin;

    public bool Descending { get; set; } = true;

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = DefaultPageSize;
}

/// <summary>
/// Handles <see cref="GetPnlProductTableQuery"/>: the same fold as the summary, then the product
/// rules, the unpaged counters and the requested slice.
/// </summary>
public sealed class GetPnlProductTableQueryHandler
    : IRequestHandler<GetPnlProductTableQuery, PnlProductTableResponse>
{
    private readonly IPnlReadStore _store;
    private readonly ITenantContext _tenantContext;
    private readonly ITenantSettingsProvider _settingsProvider;
    private readonly IProductSkuLookup _skuLookup;

    public GetPnlProductTableQueryHandler(
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
    public async Task<PnlProductTableResponse> Handle(
        GetPnlProductTableQuery request,
        CancellationToken cancellationToken)
    {
        Guid tenantId = _tenantContext.TenantId
            ?? throw new UnauthorizedAccessException("Пользователь не аутентифицирован.");

        TenantAccountingSettings settings =
            await _settingsProvider.GetAccountingSettingsAsync(tenantId, cancellationToken);

        PnlLineFilter filter = new()
        {
            ChannelId = request.Filter.ChannelId,
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

        // The unlinked group is money of the period, not a product: it gets its own line above the
        // table and takes no part in the counters, the search or the sort.
        PnlSummaryRowView? unlinkedRow = rows.FirstOrDefault(row => row.IsUnlinkedProduct);

        List<PnlProductRow> products = rows
            .Where(row => !row.IsUnlinkedProduct)
            .Select(PnlProductTable.Map)
            .ToList();

        PnlProductSummary summary = PnlProductTable.Summarize(products);

        string? search = PnlProductTable.NormalizeSearch(request.Search);
        bool labelAll = search != null || request.SortBy == PnlProductSortKey.Sku;

        // Search and SKU sort need every label; any other order needs only the page it serves, so a
        // product outside the page costs no catalogue lookup.
        if (labelAll)
        {
            await PnlSkuLabeler.ApplyAsync(_skuLookup, products, cancellationToken);
        }

        List<PnlProductRow> searched = PnlProductTable.ApplySearch(products, search);
        PnlProductSegmentCounts counts = PnlProductTable.CountSegments(searched);

        List<PnlProductRow> segmentRows = searched
            .Where(row => PnlProductTable.IsInSegment(row, request.Segment))
            .ToList();

        List<PnlProductRow> sorted = PnlProductTable.Sort(
            segmentRows,
            request.SortBy,
            request.Descending);

        (List<PnlProductRow> items, int page) = PnlProductTable.Slice(
            sorted,
            request.Page,
            request.PageSize);

        if (!labelAll)
        {
            await PnlSkuLabeler.ApplyAsync(_skuLookup, items, cancellationToken);
        }

        return new PnlProductTableResponse
        {
            Items = items,
            TotalCount = sorted.Count,
            Page = page,
            PageSize = request.PageSize,
            Segment = request.Segment,
            Search = search,
            SortBy = request.SortBy,
            Descending = request.Descending,
            Counts = counts,
            Summary = summary,
            Unlinked = unlinkedRow == null
                ? null
                : new PnlUnlinkedSales
                {
                    Lines = unlinkedRow.Lines,
                    RevenueAmount = unlinkedRow.RevenueAmount,
                    NetRevenueAmount = unlinkedRow.NetRevenueAmount,
                },
            Currency = settings.Currency,
            ExcludedByCurrency = aggregation.ExcludedCurrencies.Sum(exclusion => exclusion.Lines),
            ExcludedCurrencies = aggregation.ExcludedCurrencies,
        };
    }
}
