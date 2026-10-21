using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of IOrdersBackfillStateRepository.
/// </summary>
public class OrdersBackfillStateRepository : IOrdersBackfillStateRepository
{
    private readonly IntegrationsDbContext _context;

    /// <summary>
    /// Initializes a new instance of the <see cref="OrdersBackfillStateRepository"/> class.
    /// </summary>
    public OrdersBackfillStateRepository(IntegrationsDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public Task<OrdersBackfillState?> GetAsync(Guid connectionId, CancellationToken cancellationToken)
    {
        return _context.OrdersBackfillStates
            .FirstOrDefaultAsync(s => s.ConnectionId == connectionId, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task UpsertAsync(OrdersBackfillState state, CancellationToken cancellationToken)
    {
        OrdersBackfillState? existing = await _context.OrdersBackfillStates
            .FirstOrDefaultAsync(s => s.ConnectionId == state.ConnectionId, cancellationToken);

        if (existing is null)
        {
            if (state.Id == Guid.Empty)
            {
                state.Id = Guid.NewGuid();
            }

            _context.OrdersBackfillStates.Add(state);
        }
        else
        {
            existing.NextDayToFetch = state.NextDayToFetch;
            existing.OldestDayInScope = state.OldestDayInScope;
            existing.IsCompleted = state.IsCompleted;
            existing.CompletedAt = state.CompletedAt;
            existing.CatchUpNextDay = state.CatchUpNextDay;
            existing.HandoverWatermark = state.HandoverWatermark;
        }

        await _context.SaveChangesAsync(cancellationToken);
    }
}
