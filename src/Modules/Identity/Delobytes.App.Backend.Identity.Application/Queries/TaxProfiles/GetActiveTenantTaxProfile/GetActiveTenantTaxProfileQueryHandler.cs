using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Identity.Application.Queries.TaxProfiles.GetActiveTenantTaxProfile;

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
