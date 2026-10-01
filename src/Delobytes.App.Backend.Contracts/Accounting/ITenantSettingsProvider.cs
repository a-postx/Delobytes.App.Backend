namespace Delobytes.App.Backend.Contracts.Accounting;

/// <summary>
/// Провайдер настроек учёта и налогового профиля тенанта для других модулей.
/// </summary>
public interface ITenantSettingsProvider
{
    /// <summary>
    /// Возвращает валюту и часовой пояс тенанта.
    /// </summary>
    /// <param name="tenantId">Идентификатор тенанта.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Настройки учёта тенанта.</returns>
    Task<TenantAccountingSettings> GetAccountingSettingsAsync(Guid tenantId, CancellationToken cancellationToken);

    /// <summary>
    /// Возвращает налоговый профиль, действующий на указанный момент времени.
    /// </summary>
    /// <param name="tenantId">Идентификатор тенанта.</param>
    /// <param name="moment">Момент времени, на который подбирается профиль.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>
    /// Профиль с максимальным <c>ValidFrom &lt;= </c> даты момента, либо <c>null</c>,
    /// если на эту дату ни одного профиля нет.
    /// </returns>
    Task<TenantTaxProfileSnapshot?> GetTaxProfileAtAsync(Guid tenantId, DateTimeOffset moment, CancellationToken cancellationToken);
}
