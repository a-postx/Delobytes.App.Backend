using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Contracts.Tenancy;
using Delobytes.App.Backend.Identity.Domain.Entities;
using Delobytes.App.Backend.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Delobytes.App.Backend.Identity.Infrastructure.Services;

/// <summary>
/// Реализация публичного контракта перечисления активных тенантов.
/// </summary>
public class TenantDirectory : ITenantDirectory
{
    private readonly IdentityDbContext _context;

    /// <summary>
    /// Initializes a new instance of the <see cref="TenantDirectory"/> class.
    /// </summary>
    /// <param name="context">Identity DbContext.</param>
    public TenantDirectory(IdentityDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public Task<List<Guid>> GetActiveTenantIdsAsync(Guid? afterTenantId, int pageSize, CancellationToken cancellationToken)
    {
        if (pageSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize), pageSize, "Page size must be positive.");
        }

        return BuildPageQuery(_context, afterTenantId, pageSize).ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Строит запрос страницы. Порядок и сравнение курсора обязаны выполняться в БД: uuid
    /// в PostgreSQL упорядочен побайтово, а <see cref="Guid.CompareTo(Guid)"/> в .NET — иначе,
    /// поэтому курсор нельзя сравнивать в памяти после материализации.
    /// </summary>
    internal static IQueryable<Guid> BuildPageQuery(IdentityDbContext context, Guid? afterTenantId, int pageSize)
    {
        IQueryable<Tenant> query = context.Tenants.Where(t => t.IsActive);

        if (afterTenantId.HasValue)
        {
            Guid cursor = afterTenantId.Value;
            query = query.Where(t => t.Id.CompareTo(cursor) > 0);
        }

        return query
            .OrderBy(t => t.Id)
            .Select(t => t.Id)
            .Take(pageSize);
    }
}
