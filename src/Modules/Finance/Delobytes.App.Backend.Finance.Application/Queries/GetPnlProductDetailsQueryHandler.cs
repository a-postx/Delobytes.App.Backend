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

/// <summary>Product sheet: the product, its channels and the documents it still waits for.</summary>
public sealed class PnlProductDetailsResponse
{
    /// <summary>Gets or sets the product; null when it has no lines in the requested range.</summary>
    public PnlProductRow? Product { get; set; }

    public List<PnlProductChannelRow> Channels { get; set; } = new();

    public PnlProductDocumentGaps Documents { get; set; } = new();

    public string Currency { get; set; } = default!;
}

/// <summary>One channel the product sold through.</summary>
public sealed class PnlProductChannelRow
{
    public Guid? ChannelId { get; set; }

    public decimal? RevenueAmount { get; set; }

    /// <summary>Gets or sets the share of the product's positive channel revenue, in percent.</summary>
    public decimal? RevenueSharePercent { get; set; }

    public decimal? Margin { get; set; }

    public decimal? ContributionMarginPercent { get; set; }

    public PnlProductCostCoverage CostCoverage { get; set; }
}

/// <summary>Documents the product still waits for, split by whether their window is still open.</summary>
public sealed class PnlProductDocumentGaps
{
    public PnlDocumentGapGroup Pending { get; set; } = new();

    public PnlDocumentGapGroup Overdue { get; set; } = new();

    public decimal? ControlDifferenceAmount { get; set; }

    public int LinesWithControlDifference { get; set; }
}

/// <summary>Revenue and documents of one completeness state.</summary>
public sealed class PnlDocumentGapGroup
{
    public int Lines { get; set; }

    public decimal RevenueAmount { get; set; }

    public DateTimeOffset? EarliestExpectedBy { get; set; }

    public List<PnlMissingDocumentCount> MissingDocuments { get; set; } = new();
}

/// <summary>How many lines of a group wait for one document type.</summary>
public sealed class PnlMissingDocumentCount
{
    public string Type { get; set; } = default!;

    public int Lines { get; set; }
}

/// <summary>Drill-down of one product of the table.</summary>
public sealed class GetPnlProductDetailsQuery : IRequest<PnlProductDetailsResponse>
{
    public Guid ProductId { get; set; }

    public PnlQueryFilter Filter { get; set; } = new();
}

/// <summary>
/// Handles <see cref="GetPnlProductDetailsQuery"/>: the product fold, the channel fold and the
/// document pass, all scoped to one product and one range.
/// </summary>
public sealed class GetPnlProductDetailsQueryHandler
    : IRequestHandler<GetPnlProductDetailsQuery, PnlProductDetailsResponse>
{
    private readonly IPnlReadStore _store;
    private readonly ITenantContext _tenantContext;
    private readonly ITenantSettingsProvider _settingsProvider;
    private readonly IProductSkuLookup _skuLookup;

    public GetPnlProductDetailsQueryHandler(
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
    public async Task<PnlProductDetailsResponse> Handle(
        GetPnlProductDetailsQuery request,
        CancellationToken cancellationToken)
    {
        Guid tenantId = _tenantContext.TenantId
            ?? throw new UnauthorizedAccessException("Пользователь не аутентифицирован.");

        TenantAccountingSettings settings =
            await _settingsProvider.GetAccountingSettingsAsync(tenantId, cancellationToken);

        PnlLineFilter filter = new()
        {
            ChannelId = request.Filter.ChannelId,
            ProductId = request.ProductId,
            From = request.Filter.From,
            To = request.Filter.To,
            CompletenessStates = PnlCompletenessFilter.Parse(request.Filter.Completeness),
        };

        PnlProductRow? product = await LoadProductAsync(tenantId, settings.Currency, filter, cancellationToken);
        List<PnlProductChannelRow> channels =
            await LoadChannelsAsync(tenantId, settings.Currency, filter, cancellationToken);

        PnlProductDocumentGaps documents = await PnlDocumentGapAggregator.AggregateAsync(
            _store,
            _settingsProvider,
            tenantId,
            settings.Currency,
            filter,
            cancellationToken);

        return new PnlProductDetailsResponse
        {
            Product = product,
            Channels = channels,
            Documents = documents,
            Currency = settings.Currency,
        };
    }

    private async Task<PnlProductRow?> LoadProductAsync(
        Guid tenantId,
        string currency,
        PnlLineFilter filter,
        CancellationToken cancellationToken)
    {
        PnlSummaryAggregationResult aggregation = await PnlSummaryAggregator.AggregateAsync(
            _store,
            _settingsProvider,
            tenantId,
            currency,
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

        PnlSummaryRowView? summary = rows.FirstOrDefault();

        if (summary == null)
        {
            return null;
        }

        PnlProductRow product = PnlProductTable.Map(summary);
        await PnlSkuLabeler.ApplyAsync(
            _skuLookup,
            new List<PnlProductRow> { product },
            cancellationToken);

        return product;
    }

    private async Task<List<PnlProductChannelRow>> LoadChannelsAsync(
        Guid tenantId,
        string currency,
        PnlLineFilter filter,
        CancellationToken cancellationToken)
    {
        PnlSummaryAggregationResult aggregation = await PnlSummaryAggregator.AggregateAsync(
            _store,
            _settingsProvider,
            tenantId,
            currency,
            filter,
            PnlSummaryGroupBy.Channel,
            PnlPeriodGranularity.Total,
            null,
            cancellationToken);

        List<PnlSummaryRowView> rows = PnlSummaryProjector.BuildRows(
            aggregation.Groups,
            PnlSummaryGroupBy.Channel,
            PnlPeriodGranularity.Total,
            filter.From,
            filter.To);

        decimal positiveRevenue = rows
            .Where(row => row.RevenueAmount.HasValue && row.RevenueAmount.Value > 0m)
            .Sum(row => row.RevenueAmount!.Value);

        return rows
            .OrderByDescending(row => row.RevenueAmount)
            .ThenBy(row => row.ChannelId)
            .Select(row => new PnlProductChannelRow
            {
                ChannelId = row.ChannelId,
                RevenueAmount = row.RevenueAmount,
                RevenueSharePercent = row.RevenueAmount.HasValue && positiveRevenue > 0m
                    ? row.RevenueAmount.Value / positiveRevenue * 100m
                    : null,
                Margin = row.Margin,
                ContributionMarginPercent = row.ContributionMarginPercent,
                CostCoverage = PnlProductTable.ResolveCostCoverage(row),
            })
            .ToList();
    }
}
