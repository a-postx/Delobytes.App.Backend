using Delobytes.App.Backend.Contracts.Interfaces;

namespace Delobytes.App.Backend.Contracts.Accounting;

/// <summary>
/// Удобные обёртки над <see cref="ITenantTimeService"/> для сценариев, где тенант
/// берётся из текущего запроса.
/// </summary>
public static class TenantTimeServiceExtensions
{
    /// <summary>
    /// Возвращает текущую календарную дату тенанта из контекста запроса.
    /// </summary>
    /// <param name="service">Сервис времени тенанта.</param>
    /// <param name="tenantContext">Контекст текущего тенанта.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Сегодняшняя дата в календаре тенанта.</returns>
    /// <exception cref="UnauthorizedAccessException">В контексте нет тенанта.</exception>
    public static async Task<DateOnly> GetTodayAsync(
        this ITenantTimeService service,
        ITenantContext tenantContext,
        CancellationToken cancellationToken)
    {
        Guid tenantId = tenantContext.TenantId
            ?? throw new UnauthorizedAccessException("Пользователь не аутентифицирован.");

        return await service.GetTodayAsync(tenantId, cancellationToken);
    }
}
