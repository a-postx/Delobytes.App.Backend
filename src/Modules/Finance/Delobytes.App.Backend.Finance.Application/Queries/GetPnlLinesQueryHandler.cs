using Delobytes.App.Backend.Catalog.Contracts.Lookup;
using Delobytes.App.Backend.Contracts.Accounting;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Finance.Application.Interfaces;
using Delobytes.App.Backend.Finance.Application.ReadModels;
using MediatR;

namespace Delobytes.App.Backend.Finance.Application.Queries;

/// <summary>Response of the P&amp;L line query.</summary>
public sealed class PnlLinesResponse
{
    public List<PnlLineView> Lines { get; set; } = new();

    public string? ContinuationToken { get; set; }

    public string Currency { get; set; } = default!;

    public int ExcludedByCurrency { get; set; }
}

/// <summary>Query of the P&amp;L lines.</summary>
public sealed class GetPnlLinesQuery : IRequest<PnlLinesResponse>
{
    public PnlQueryFilter Filter { get; set; } = new();

    public int Take { get; set; } = 100;

    public string? ContinuationToken { get; set; }
}

/// <summary>Handles <see cref="GetPnlLinesQuery"/>.</summary>
public sealed class GetPnlLinesQueryHandler : IRequestHandler<GetPnlLinesQuery, PnlLinesResponse>
{
    private readonly IPnlReadStore _store;
    private readonly ITenantContext _tenantContext;
    private readonly ITenantSettingsProvider _settingsProvider;
    private readonly IProductSkuLookup _skuLookup;

    public GetPnlLinesQueryHandler(
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
    public async Task<PnlLinesResponse> Handle(GetPnlLinesQuery request, CancellationToken cancellationToken)
    {
        Guid tenantId = _tenantContext.TenantId
            ?? throw new UnauthorizedAccessException("Пользователь не аутентифицирован.");

        TenantAccountingSettings settings =
            await _settingsProvider.GetAccountingSettingsAsync(tenantId, cancellationToken);

        int take = Math.Clamp(request.Take, 1, 500);

        PnlLineFilter filter = new()
        {
            ChannelId = request.Filter.ChannelId,
            ProductId = request.Filter.ProductId,
            ChannelProductId = request.Filter.ChannelProductId,
            From = request.Filter.From,
            To = request.Filter.To,
            State = Enum.TryParse(request.Filter.State, ignoreCase: true, out Domain.Enums.PnlLineState state)
                ? state
                : null,
            CompletenessStates = PnlCompletenessFilter.Parse(request.Filter.Completeness),
        };

        PnlLinePage page = await _store.GetLinesAsync(
            settings.Currency,
            filter,
            take,
            request.ContinuationToken,
            cancellationToken);

        List<PnlLineView> views = await PnlViewFactory.BuildAsync(
            page.Rows,
            tenantId,
            _settingsProvider,
            cancellationToken);

        await EnrichSkusAsync(views, cancellationToken);

        return new PnlLinesResponse
        {
            Lines = views,
            ContinuationToken = page.NextToken,
            Currency = settings.Currency,
            ExcludedByCurrency = page.ExcludedByCurrency,
        };
    }

    // One lookup per page: identifiers are deduplicated, so two lines of the same product cost one
    // entry. A page with no product link skips the call entirely.
    private async Task EnrichSkusAsync(List<PnlLineView> views, CancellationToken cancellationToken)
    {
        Guid[] productIds = views
            .Where(view => view.ProductId.HasValue)
            .Select(view => view.ProductId!.Value)
            .Distinct()
            .ToArray();

        if (productIds.Length == 0)
        {
            return;
        }

        IReadOnlyDictionary<Guid, string> skus = await _skuLookup.GetSkusAsync(productIds, cancellationToken);

        foreach (PnlLineView view in views)
        {
            if (view.ProductId.HasValue && skus.TryGetValue(view.ProductId.Value, out string sku))
            {
                view.Sku = sku;
            }
        }
    }
}
