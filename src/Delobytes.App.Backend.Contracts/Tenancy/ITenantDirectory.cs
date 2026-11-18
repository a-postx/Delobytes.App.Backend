using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Delobytes.App.Backend.Contracts.Tenancy;

/// <summary>
/// Публичный контракт Identity для модулей и фоновых сервисов, которым нужно перечислить
/// активные тенанты. Не даёт доступа к агрегату Tenant и его домену.
/// </summary>
public interface ITenantDirectory
{
    /// <summary>
    /// Возвращает очередную страницу идентификаторов активных тенантов в порядке возрастания Id.
    /// Обход постраничный (keyset), а не через OFFSET: набор тенантов меняется во время обхода,
    /// и OFFSET на изменяемом наборе пропускает или дублирует строки.
    /// </summary>
    /// <param name="afterTenantId">
    /// Курсор: вернуть тенантов со строго большим Id. <c>null</c> — первая страница.
    /// Для следующей страницы передавайте последний Id предыдущей.
    /// </param>
    /// <param name="pageSize">Максимальный размер страницы, больше нуля.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Идентификаторы тенантов; страница короче <paramref name="pageSize"/> означает конец обхода.</returns>
    public Task<List<Guid>> GetActiveTenantIdsAsync(Guid? afterTenantId, int pageSize, CancellationToken cancellationToken);
}
