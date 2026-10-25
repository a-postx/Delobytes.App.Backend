using MediatR;
using Delobytes.App.Backend.Contracts.Accounting;
using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Domain.Entities;

namespace Delobytes.App.Backend.Identity.Application.Queries.TaxProfiles;

/// <summary>
/// Запрос действующего на указанный момент налогового профиля тенанта.
/// </summary>
public class GetActiveTenantTaxProfileQuery : IRequest<GetActiveTenantTaxProfileResponse>
{
    /// <summary>
    /// Gets or sets идентификатор тенанта.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Gets or sets момент времени; по умолчанию — текущий.
    /// </summary>
    public DateTimeOffset? At { get; set; }
}

/// <summary>
/// Ответ на запрос действующего налогового профиля.
/// </summary>
public class GetActiveTenantTaxProfileResponse
{
    /// <summary>
    /// Gets or sets признак наличия профиля на указанный момент.
    /// </summary>
    public bool Found { get; set; }

    /// <summary>
    /// Gets or sets идентификатор версии профиля.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets налоговый режим.
    /// </summary>
    public TaxRegime Regime { get; set; }

    /// <summary>
    /// Gets or sets ставка налога в процентах.
    /// </summary>
    public decimal RatePercent { get; set; }

    /// <summary>
    /// Gets or sets режим НДС.
    /// </summary>
    public VatType Vat { get; set; }

    /// <summary>
    /// Gets or sets дата начала действия версии.
    /// </summary>
    public DateOnly ValidFrom { get; set; }
}

/// <summary>
/// Handler for GetActiveTenantTaxProfileQuery.
/// </summary>
public class GetActiveTenantTaxProfileQueryHandler : IRequestHandler<GetActiveTenantTaxProfileQuery, GetActiveTenantTaxProfileResponse>
{
    private readonly ITenantTaxProfileRepository _taxProfileRepository;
    private readonly ITenantRepository _tenantRepository;

    /// <summary>
    /// Initializes a new instance of the <see cref="GetActiveTenantTaxProfileQueryHandler"/> class.
    /// </summary>
    public GetActiveTenantTaxProfileQueryHandler(
        ITenantTaxProfileRepository taxProfileRepository,
        ITenantRepository tenantRepository)
    {
        _taxProfileRepository = taxProfileRepository;
        _tenantRepository = tenantRepository;
    }

    /// <inheritdoc/>
    public async Task<GetActiveTenantTaxProfileResponse> Handle(
        GetActiveTenantTaxProfileQuery request,
        CancellationToken cancellationToken)
    {
        Tenant? tenant = await _tenantRepository.FindByIdAsync(request.TenantId, cancellationToken);

        if (tenant == null)
        {
            throw new InvalidOperationException($"Пространство с ID {request.TenantId} не найдено.");
        }

        // Дата берётся в часовом поясе тенанта, а не по UTC: заказ, сделанный в 00:30 по
        // Москве 1 января, приходит как 21:30 UTC 31 декабря и должен получить профиль
        // нового года, а не предыдущего.
        DateTimeOffset moment = request.At ?? DateTimeOffset.UtcNow;
        DateOnly date = ResolveTenantDate(tenant.TimeZone, moment);

        TenantTaxProfile? profile = await _taxProfileRepository.GetAtDateAsync(request.TenantId, date, cancellationToken);

        // Отсутствие налоговой настройки — нормальное состояние нового тенанта,
        // поэтому это не ошибка, а Found = false.
        if (profile == null)
        {
            return new GetActiveTenantTaxProfileResponse { Found = false };
        }

        return new GetActiveTenantTaxProfileResponse
        {
            Found = true,
            Id = profile.Id,
            Regime = profile.Regime,
            RatePercent = profile.RatePercent,
            Vat = profile.Vat,
            ValidFrom = profile.ValidFrom,
        };
    }

    private static DateOnly ResolveTenantDate(string timeZoneId, DateTimeOffset moment)
    {
        try
        {
            TimeZoneInfo timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(moment, timeZone).DateTime);
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
}
