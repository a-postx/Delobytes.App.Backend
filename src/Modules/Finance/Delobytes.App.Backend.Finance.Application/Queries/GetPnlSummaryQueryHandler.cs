using Delobytes.App.Backend.Contracts.Accounting;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Finance.Application.Interfaces;
using Delobytes.App.Backend.Finance.Application.ReadModels;
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

    /// <summary>Gets or sets the margin before tax. Null for a group whose costs are unknown or incomplete.</summary>
    public decimal? Margin { get; set; }

    /// <summary>Gets or sets the margin net of the estimated tax; null when no row of the group has a tax profile.</summary>
    public decimal? TaxAdjustedMargin { get; set; }

    public decimal? ControlDifferenceAmount { get; set; }
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
    private readonly ITenantTimeService _timeService;

    public GetPnlSummaryQueryHandler(
        IPnlReadStore store,
        ITenantContext tenantContext,
        ITenantSettingsProvider settingsProvider,
        ITenantTimeService timeService)
    {
        _store = store;
        _tenantContext = tenantContext;
        _settingsProvider = settingsProvider;
        _timeService = timeService;
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
        };

        IReadOnlyList<PnlLineRow> rows = await _store.GetForSummaryAsync(settings.Currency, filter, cancellationToken);

        List<PnlLineView> views = await PnlViewFactory.BuildAsync(
            rows,
            tenantId,
            _settingsProvider,
            _timeService,
            cancellationToken);

        List<PnlSummaryRowView> summary = views
            .Where(v => v.SaleDate.HasValue)
            .GroupBy(v => (v.ProductId, PeriodStart: PeriodOf(v.SaleDate!.Value, request.Granularity)))
            .Select(group => new PnlSummaryRowView
            {
                ProductId = group.Key.ProductId,
                IsUnlinkedProduct = group.Key.ProductId == null,
                PeriodStart = group.Key.PeriodStart,
                Lines = group.Count(),
                RevenueAmount = Sum(group.Select(v => v.RevenueAmount)),
                NetRevenueAmount = Sum(group.Select(v => v.NetRevenueAmount)),
                ProductionCostAmount = Sum(group.Select(v => v.ProductionCostAmount)),
                EstimatedTax = Sum(group.Select(v => v.EstimatedTax)),
                Margin = Sum(group.Select(v => v.Margin)),
                TaxAdjustedMargin = Sum(group.Select(v => v.TaxAdjustedMargin)),
                ControlDifferenceAmount = Sum(rows
                    .Where(r => group.Any(v => v.OrderLineId == r.OrderLineId))
                    .Select(r => r.ControlDifferenceAmount)),
            })
            .OrderBy(r => r.PeriodStart)
            .ThenBy(r => r.ProductId)
            .ToList();

        return new PnlSummaryResponse
        {
            Rows = summary,
            Currency = settings.Currency,
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

    private static decimal? Sum(IEnumerable<decimal?> values)
    {
        List<decimal> present = values.Where(v => v.HasValue).Select(v => v!.Value).ToList();

        return present.Count == 0 ? null : present.Sum();
    }
}
