using Delobytes.App.Backend.Finance.Application.Interfaces;
using Delobytes.App.Backend.Finance.Domain.Entities;
using Delobytes.App.Backend.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Delobytes.App.Backend.Finance.Infrastructure.Persistence.Stores;

/// <summary>
/// EF Core implementation of <see cref="IPnlLineStore"/>.
/// </summary>
public class PnlLineStore : IPnlLineStore
{
    private readonly FinanceDbContext _context;

    /// <summary>
    /// Initializes a new instance of the <see cref="PnlLineStore"/> class.
    /// </summary>
    /// <param name="context">Finance database context.</param>
    public PnlLineStore(FinanceDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PnlLine>> LoadAsync(
        IReadOnlyCollection<Guid> orderLineIds,
        CancellationToken cancellationToken)
    {
        if (orderLineIds.Count == 0)
        {
            return Array.Empty<PnlLine>();
        }

        List<Guid> ids = orderLineIds.ToList();

        return await _context.PnlLines
            .Where(l => ids.Contains(l.OrderLineId))
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public void Add(PnlLine line)
    {
        _context.PnlLines.Add(line);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Guid>> LoadOrderLineIdsAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        return await _context.PnlLines
            .AsNoTracking()
            .Where(l => l.SaleDate != null && l.SaleDate >= from && l.SaleDate <= to)
            .Select(l => l.OrderLineId)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return _context.SaveChangesAsync(cancellationToken);
    }
}
