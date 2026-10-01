using Delobytes.App.Backend.Identity.Domain.Entities;

namespace Delobytes.App.Backend.Identity.Application.Interfaces;

/// <summary>
/// Репозиторий версий налогового профиля тенанта.
/// Все выборки ограничены одним тенантом.
/// </summary>
public interface ITenantTaxProfileRepository
{
    /// <summary>
    /// Возвращает все версии профиля тенанта, отсортированные по дате начала по убыванию.
    /// </summary>
    /// <param name="tenantId">Идентификатор тенанта.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Список версий профиля.</returns>
    Task<IReadOnlyList<TenantTaxProfile>> GetByTenantAsync(Guid tenantId, CancellationToken cancellationToken);

    /// <summary>
    /// Возвращает версию профиля, действующую на указанную дату.
    /// </summary>
    /// <param name="tenantId">Идентификатор тенанта.</param>
    /// <param name="date">Дата (в часовом поясе тенанта).</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Ближайшая версия с <c>ValidFrom &lt;= date</c>, либо <c>null</c>.</returns>
    Task<TenantTaxProfile?> GetAtDateAsync(Guid tenantId, DateOnly date, CancellationToken cancellationToken);

    /// <summary>
    /// Возвращает версию профиля по идентификатору в пределах тенанта.
    /// </summary>
    /// <param name="tenantId">Идентификатор тенанта.</param>
    /// <param name="id">Идентификатор версии профиля.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Версия профиля либо <c>null</c>.</returns>
    Task<TenantTaxProfile?> FindByIdAsync(Guid tenantId, Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Возвращает последнюю (с максимальным <c>ValidFrom</c>) версию профиля тенанта.
    /// </summary>
    /// <param name="tenantId">Идентификатор тенанта.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Версия профиля либо <c>null</c>, если версий нет.</returns>
    Task<TenantTaxProfile?> GetLatestAsync(Guid tenantId, CancellationToken cancellationToken);

    /// <summary>
    /// Добавляет версию профиля.
    /// </summary>
    /// <param name="profile">Версия профиля.</param>
    void Add(TenantTaxProfile profile);

    /// <summary>
    /// Удаляет версию профиля.
    /// </summary>
    /// <param name="profile">Версия профиля.</param>
    void Remove(TenantTaxProfile profile);

    /// <summary>
    /// Сохраняет изменения.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Количество затронутых строк.</returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
