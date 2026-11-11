namespace Delobytes.App.Backend.Contracts.Accounting;

/// <summary>
/// Часовой пояс тенанта и календарные даты, посчитанные в нём.
/// </summary>
public interface ITenantTimeService
{
    /// <summary>
    /// Возвращает часовой пояс тенанта.
    /// </summary>
    /// <param name="tenantId">Идентификатор тенанта.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Часовой пояс тенанта.</returns>
    public Task<TimeZoneInfo> GetTimeZoneAsync(Guid tenantId, CancellationToken cancellationToken);

    /// <summary>
    /// Возвращает текущую календарную дату тенанта.
    /// </summary>
    /// <param name="tenantId">Идентификатор тенанта.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Сегодняшняя дата в календаре тенанта.</returns>
    public Task<DateOnly> GetTodayAsync(Guid tenantId, CancellationToken cancellationToken);
}
