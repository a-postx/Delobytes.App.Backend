using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Contracts.Accounting;
using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Domain.Entities;

namespace Delobytes.App.Backend.Identity.Infrastructure.Services;

/// <summary>
/// Реализация провайдера настроек учёта тенанта для других модулей.
/// </summary>
public class TenantSettingsProvider : ITenantSettingsProvider
{
    private readonly ITenantRepository _tenantRepository;
    private readonly ITenantTaxProfileRepository _taxProfileRepository;

    /// <summary>
    /// Initializes a new instance of the <see cref="TenantSettingsProvider"/> class.
    /// </summary>
    public TenantSettingsProvider(
        ITenantRepository tenantRepository,
        ITenantTaxProfileRepository taxProfileRepository)
    {
        _tenantRepository = tenantRepository;
        _taxProfileRepository = taxProfileRepository;
    }

    /// <inheritdoc/>
    public async Task<TenantAccountingSettings> GetAccountingSettingsAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        Tenant? tenant = await _tenantRepository.FindByIdAsync(tenantId, cancellationToken);

        if (tenant == null)
        {
            throw new InvalidOperationException($"Пространство с ID {tenantId} не найдено.");
        }

        return new TenantAccountingSettings
        {
            Currency = tenant.Currency,
            TimeZoneId = tenant.TimeZone,
        };
    }

    /// <inheritdoc/>
    public async Task<TenantTaxProfileSnapshot?> GetTaxProfileAtAsync(Guid tenantId, DateTimeOffset moment, CancellationToken cancellationToken)
    {
        Tenant? tenant = await _tenantRepository.FindByIdAsync(tenantId, cancellationToken);

        if (tenant == null)
        {
            throw new InvalidOperationException($"Пространство с ID {tenantId} не найдено.");
        }

        TimeZoneInfo timeZone;

        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(tenant.TimeZone);
        }
        catch (TimeZoneNotFoundException ex)
        {
            throw new InvalidOperationException($"Часовой пояс '{tenant.TimeZone}' не найден в системе.", ex);
        }
        catch (InvalidTimeZoneException ex)
        {
            throw new InvalidOperationException($"Часовой пояс '{tenant.TimeZone}' содержит некорректные данные.", ex);
        }

        // Дата профиля — календарная дата тенанта, а не UTC: иначе граница периода
        // смещается на сутки у всех, кто живёт восточнее Гринвича.
        DateOnly date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(moment, timeZone).DateTime);

        TenantTaxProfile? profile = await _taxProfileRepository.GetAtDateAsync(tenantId, date, cancellationToken);

        if (profile == null)
        {
            return null;
        }

        return new TenantTaxProfileSnapshot
        {
            Regime = profile.Regime,
            RatePercent = profile.RatePercent,
            Vat = profile.Vat,
            ValidFrom = profile.ValidFrom,
        };
    }
}
