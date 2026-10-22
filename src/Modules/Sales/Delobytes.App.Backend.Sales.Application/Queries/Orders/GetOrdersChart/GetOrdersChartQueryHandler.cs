using Delobytes.App.Backend.Contracts.Accounting;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Sales.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Sales.Application.Models;
using Delobytes.App.Backend.Sales.Application.Services;
using FluentValidation;
using FluentValidation.Results;
using MediatR;

namespace Delobytes.App.Backend.Sales.Application.Queries.Orders.GetOrdersChart;

/// <summary>
/// Resolves the period defaults in the tenant's time zone, reads the tenant currency, asks the
/// repository for the flat order-line facts of the resulting UTC window and buckets them in memory.
/// </summary>
public class GetOrdersChartQueryHandler : IRequestHandler<GetOrdersChartQuery, GetOrdersChartResponse>
{
    private const int DefaultRangeDays = 7;
    private const int MaxRangeDays = 366;

    private readonly IOrderStatsRepository _repository;
    private readonly ITenantSettingsProvider _settingsProvider;
    private readonly ITenantContext _tenantContext;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="GetOrdersChartQueryHandler"/> class.
    /// </summary>
    /// <param name="repository">Order stats repository.</param>
    /// <param name="settingsProvider">Tenant accounting settings provider.</param>
    /// <param name="tenantContext">Current tenant context.</param>
    /// <param name="timeProvider">Clock used to resolve "today" in the tenant's time zone.</param>
    public GetOrdersChartQueryHandler(
        IOrderStatsRepository repository,
        ITenantSettingsProvider settingsProvider,
        ITenantContext tenantContext,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _settingsProvider = settingsProvider;
        _tenantContext = tenantContext;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc/>
    public async Task<GetOrdersChartResponse> Handle(GetOrdersChartQuery request, CancellationToken cancellationToken)
    {
        Guid tenantId = _tenantContext.TenantId
            ?? throw new UnauthorizedAccessException("Пользователь не аутентифицирован.");

        TenantAccountingSettings settings = await _settingsProvider.GetAccountingSettingsAsync(tenantId, cancellationToken);
        TimeZoneInfo timeZone = ResolveTimeZone(settings.TimeZoneId);

        DateOnly today = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(_timeProvider.GetUtcNow(), timeZone).DateTime);

        DateOnly to = request.To ?? today;
        DateOnly from = request.From ?? to.AddDays(-(DefaultRangeDays - 1));

        EnsureValidRange(from, to);

        ChartGranularity granularity = request.Granularity
            ?? SalesChartBucketing.ResolveAdaptiveGranularity(from, to);

        DateTimeOffset fromUtc = ToUtc(from, timeZone);
        DateTimeOffset toUtcExclusive = ToUtc(to.AddDays(1), timeZone);

        IReadOnlyList<OrderLineFact> facts = await _repository.GetLineFactsAsync(
            fromUtc,
            toUtcExclusive,
            settings.Currency,
            request.ChannelId,
            cancellationToken);

        IReadOnlyList<OrderDailyTotal> dailyTotals = SalesChartBucketing.GroupByLocalDay(facts, timeZone);
        List<OrdersChartPoint> points = SalesChartBucketing.BuildSeries(dailyTotals, from, to, granularity).ToList();

        return new GetOrdersChartResponse
        {
            From = from,
            To = to,
            Granularity = granularity,
            Currency = settings.Currency,
            ChannelId = request.ChannelId,
            Totals = new OrdersChartTotals
            {
                Quantity = points.Sum(point => point.Quantity),
                Amount = decimal.Round(points.Sum(point => point.Amount), 2),
            },
            Points = points,
        };
    }

    // Defaults are resolved before this runs, so the validator cannot have seen these values;
    // failures are built by hand to keep the violating field name in the error envelope.
    private static void EnsureValidRange(DateOnly from, DateOnly to)
    {
        List<ValidationFailure> failures = new();

        if (to < from)
        {
            failures.Add(new ValidationFailure(nameof(GetOrdersChartQuery.To), "Дата окончания периода не может быть раньше даты начала."));
        }

        int rangeDays = to.DayNumber - from.DayNumber + 1;

        if (rangeDays > MaxRangeDays)
        {
            failures.Add(new ValidationFailure(
                nameof(GetOrdersChartQuery.To),
                $"Период не может превышать {MaxRangeDays} дней."));
        }

        if (failures.Count != 0)
        {
            throw new ValidationException(failures);
        }
    }

    private static TimeZoneInfo ResolveTimeZone(string timeZoneId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException ex)
        {
            throw new InvalidOperationException($"Часовой пояс '{timeZoneId}' не найден в системе.", ex);
        }
        catch (InvalidTimeZoneException ex)
        {
            throw new InvalidOperationException($"Часовой пояс '{timeZoneId}' содержит некорректные данные.", ex);
        }
    }

    private static DateTimeOffset ToUtc(DateOnly localDate, TimeZoneInfo timeZone)
    {
        DateTime localMidnight = DateTime.SpecifyKind(
            localDate.ToDateTime(TimeOnly.MinValue),
            DateTimeKind.Unspecified);

        return TimeZoneInfo.ConvertTimeToUtc(localMidnight, timeZone);
    }
}
