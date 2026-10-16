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

    /// <summary>
    /// Возвращает лимит одновременно выполняемых фоновых задач (JobRun) для тенанта.
    /// Используется планировщиком/воркером фоновых заданий Integrations (Stage 11) и не
    /// относится к налоговому/валютному профилю, но размещён здесь, а не в отдельном
    /// интерфейсе, т.к. ITenantSettingsProvider уже служит единой точкой чтения настроек
    /// тенанта модулями, не имеющими прямой зависимости на Identity.
    /// </summary>
    /// <param name="tenantId">Идентификатор тенанта.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Максимальное число одновременно выполняемых JobRun для тенанта.</returns>
    Task<int> GetMaxBackgroundJobsParallelismAsync(Guid tenantId, CancellationToken cancellationToken);
}
