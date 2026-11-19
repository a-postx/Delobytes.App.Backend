using Delobytes.App.Backend.Contracts.Accounting;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Finance.Application.Interfaces;
using Delobytes.App.Backend.Finance.Application.ReadModels;
using MediatR;

namespace Delobytes.App.Backend.Finance.Application.Queries;

/// <summary>Granularity of the P&amp;L summary.</summary>
public enum PnlPeriodGranularity
{
    Day = 0,
    Week = 1,
    Month = 2,
}

/// <summary>Filters shared by the read queries.</summary>
public sealed class PnlQueryFilter
{
    public Guid? ChannelId { get; set; }

    public Guid? ProductId { get; set; }

    public DateOnly? From { get; set; }

    public DateOnly? To { get; set; }

    public string? State { get; set; }
}

/// <summary>One P&amp;L line returned by the read model.</summary>
public sealed class PnlLineView
{
    public Guid OrderLineId { get; set; }

    public Guid? ChannelId { get; set; }

    public Guid? ProductId { get; set; }

    public bool HasProductLink { get; set; }

    public DateOnly? SaleDate { get; set; }

    public string Currency { get; set; } = default!;

    public decimal? RevenueAmount { get; set; }

    public decimal? CompensationAmount { get; set; }

    public decimal? NetRevenueAmount { get; set; }

    public decimal? ProductionCostAmount { get; set; }

    public decimal? EstimatedVat { get; set; }

    public decimal? EstimatedTax { get; set; }

    public decimal? MarketplaceFee { get; set; }

    public decimal? Margin { get; set; }

    public decimal? MarginPercent { get; set; }

    public decimal? Roi { get; set; }

    public bool CostComplete { get; set; }

    public string? CostStatus { get; set; }

    public bool ComponentsReconciled { get; set; }
}

/// <summary>Response of the P&amp;L line query.</summary>
public sealed class PnlLinesResponse
{
    public List<PnlLineView> Lines { get; set; } = new();

    public string? ContinuationToken { get; set; }

    public string Currency { get; set; } = default!;

    public int ExcludedByCurrency { get; set; }
}

internal static class PnlViewFactory
{
    public static async Task<List<PnlLineView>> BuildAsync(
        IReadOnlyList<PnlLineRow> rows,
        Guid tenantId,
        ITenantSettingsProvider settingsProvider,
        ITenantTimeService timeService,
        CancellationToken cancellationToken)
    {
        TimeZoneInfo timeZone = await timeService.GetTimeZoneAsync(tenantId, cancellationToken);
        Dictionary<DateOnly, TenantTaxProfileSnapshot?> profiles = new();

        List<PnlLineView> views = new(rows.Count);

        foreach (PnlLineRow row in rows)
        {
            TenantTaxProfileSnapshot? profile = null;

            if (row.SaleDate.HasValue)
            {
                if (!profiles.TryGetValue(row.SaleDate.Value, out profile))
                {
                    profile = await settingsProvider.GetTaxProfileAtAsync(
                        tenantId,
                        TenantTimeZones.StartOfDayUtc(row.SaleDate.Value, timeZone),
                        cancellationToken);

                    profiles.Add(row.SaleDate.Value, profile);
                }
            }

            decimal? estimatedVat = profile == null
                ? null
                : PnlTaxCalculator.EstimatedVat(row.RevenueAmount, profile.Vat);

            decimal? estimatedTax = PnlTaxCalculator.EstimatedTax(row.RevenueAmount, profile);
            decimal? fee = PnlTaxCalculator.MarketplaceFee(
                row.CommissionAmount, row.AcquiringAmount, row.DeliveryFeeAmount);

            decimal? margin = PnlTaxCalculator.Margin(
                row.NetRevenueAmount, row.ProductionCostAmount, estimatedTax);

            views.Add(new PnlLineView
            {
                OrderLineId = row.OrderLineId,
                ChannelId = row.ChannelId,
                ProductId = row.ProductId,
                HasProductLink = row.ProductId.HasValue,
                SaleDate = row.SaleDate,
                Currency = row.Currency,
                RevenueAmount = row.RevenueAmount,
                CompensationAmount = row.CompensationAmount,
                NetRevenueAmount = row.NetRevenueAmount,
                ProductionCostAmount = row.ProductionCostAmount,
                EstimatedVat = estimatedVat,
                EstimatedTax = estimatedTax,
                MarketplaceFee = fee,
                Margin = margin,
                MarginPercent = PnlTaxCalculator.MarginPercent(margin, row.RevenueAmount),
                Roi = PnlTaxCalculator.Roi(margin, row.ProductionCostAmount),
                CostComplete = row.ProductionCostIsComplete == true,
                CostStatus = row.CostStatus?.ToString(),
                ComponentsReconciled =
                    row.ControlDifferenceAmount.HasValue
                    && Math.Abs(row.ControlDifferenceAmount.Value) <= 0.01m,
            });
        }

        return views;
    }
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
    private readonly ITenantTimeService _timeService;

    public GetPnlLinesQueryHandler(
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
            From = request.Filter.From,
            To = request.Filter.To,
            State = Enum.TryParse(request.Filter.State, ignoreCase: true, out Domain.Enums.PnlLineState state)
                ? state
                : null,
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
            _timeService,
            cancellationToken);

        return new PnlLinesResponse
        {
            Lines = views,
            ContinuationToken = page.NextToken,
            Currency = settings.Currency,
            ExcludedByCurrency = page.ExcludedByCurrency,
        };
    }
}

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

    public decimal? Margin { get; set; }

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

/// <summary>Period diagnostics.</summary>
public sealed class GetPnlDiagnosticsQuery : IRequest<PnlDiagnosticsResult>
{
    public DateOnly From { get; set; }

    public DateOnly To { get; set; }
}

/// <summary>Handles <see cref="GetPnlDiagnosticsQuery"/>.</summary>
public sealed class GetPnlDiagnosticsQueryHandler : IRequestHandler<GetPnlDiagnosticsQuery, PnlDiagnosticsResult>
{
    private readonly IPnlReadStore _store;

    public GetPnlDiagnosticsQueryHandler(IPnlReadStore store)
    {
        _store = store;
    }

    /// <inheritdoc/>
    public Task<PnlDiagnosticsResult> Handle(GetPnlDiagnosticsQuery request, CancellationToken cancellationToken)
    {
        if (request.From > request.To)
        {
            throw new ArgumentException("The period start must not be after its end.", nameof(request));
        }

        return _store.GetDiagnosticsAsync(request.From, request.To, cancellationToken);
    }
}
