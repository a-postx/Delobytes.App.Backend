using Delobytes.App.Backend.Contracts.Accounting;

namespace Delobytes.App.Backend.Identity.Infrastructure.Services;

/// <summary>
/// Часовой пояс тенанта и его текущая календарная дата.
/// </summary>
public class TenantTimeService : ITenantTimeService
{
    private readonly ITenantSettingsProvider _settingsProvider;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="TenantTimeService"/> class.
    /// </summary>
    /// <param name="settingsProvider">Провайдер настроек учёта тенанта.</param>
    /// <param name="timeProvider">Источник текущего времени.</param>
    public TenantTimeService(ITenantSettingsProvider settingsProvider, TimeProvider timeProvider)
    {
        _settingsProvider = settingsProvider;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc/>
    public async Task<TimeZoneInfo> GetTimeZoneAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        TenantAccountingSettings settings = await _settingsProvider.GetAccountingSettingsAsync(tenantId, cancellationToken);

        return TenantTimeZones.Resolve(settings.TimeZoneId);
    }

    /// <inheritdoc/>
    public async Task<DateOnly> GetTodayAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        TimeZoneInfo timeZone = await GetTimeZoneAsync(tenantId, cancellationToken);

        return TenantTimeZones.DateAt(_timeProvider.GetUtcNow(), timeZone);
    }
}
