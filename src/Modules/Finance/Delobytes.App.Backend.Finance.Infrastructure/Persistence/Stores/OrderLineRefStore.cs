using Delobytes.App.Backend.Finance.Application.Interfaces;
using Delobytes.App.Backend.Finance.Application.Models;
using Delobytes.App.Backend.Finance.Domain.Entities;
using Delobytes.App.Backend.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Delobytes.App.Backend.Finance.Infrastructure.Persistence.Stores;

/// <summary>
/// EF Core implementation of <see cref="IOrderLineRefStore"/>.
/// </summary>
public class OrderLineRefStore : IOrderLineRefStore
{
    private readonly FinanceDbContext _context;

    /// <summary>
    /// Initializes a new instance of the <see cref="OrderLineRefStore"/> class.
    /// </summary>
    /// <param name="context">Finance database context.</param>
    public OrderLineRefStore(FinanceDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<OrderLineRef>> LoadAsync(
        IReadOnlyCollection<Guid> orderLineIds,
        CancellationToken cancellationToken)
    {
        if (orderLineIds.Count == 0)
        {
            return Array.Empty<OrderLineRef>();
        }

        List<Guid> ids = orderLineIds.ToList();

        return await _context.OrderLineRefs
            .AsNoTracking()
            .Where(r => ids.Contains(r.OrderLineId))
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<OrderLineRef>> LoadByAttributionAsync(
        IReadOnlyCollection<OrderLineAttributionKey> keys,
        CancellationToken cancellationToken)
    {
        if (keys.Count == 0)
        {
            return Array.Empty<OrderLineRef>();
        }

        List<OrderLineRef> result = new();

        List<OrderLineAttributionKey> lineKeys = keys.Where(k => k.ExternalLineId != null).ToList();

        if (lineKeys.Count > 0)
        {
            List<Guid> channelIds = lineKeys.Select(k => k.ChannelId).Distinct().ToList();
            List<string> externalLineIds = lineKeys.Select(k => k.ExternalLineId!).Distinct().ToList();

            List<OrderLineRef> lineRefs = await _context.OrderLineRefs
                .AsNoTracking()
                .Where(r =>
                    r.ExternalLineId != null
                    && channelIds.Contains(r.ChannelId)
                    && externalLineIds.Contains(r.ExternalLineId))
                .ToListAsync(cancellationToken);

            HashSet<OrderLineAttributionKey> lookup = lineKeys.ToHashSet();

            result.AddRange(lineRefs.Where(r => lookup.Contains(
                OrderLineAttributionKey.ForLine(r.ChannelId, r.ExternalLineId!))));
        }

        List<OrderLineAttributionKey> orderKeys = keys.Where(k => k.ExternalLineId == null).ToList();

        if (orderKeys.Count > 0)
        {
            List<Guid> channelIds = orderKeys.Select(k => k.ChannelId).Distinct().ToList();
            List<string> externalOrderIds = orderKeys.Select(k => k.ExternalOrderId!).Distinct().ToList();
            List<string> productIds = orderKeys.Select(k => k.ExternalProductId!).Distinct().ToList();

            List<OrderLineRef> orderRefs = await _context.OrderLineRefs
                .AsNoTracking()
                .Where(r =>
                    channelIds.Contains(r.ChannelId)
                    && externalOrderIds.Contains(r.ExternalOrderId)
                    && productIds.Contains(r.ExternalProductId))
                .ToListAsync(cancellationToken);

            HashSet<OrderLineAttributionKey> lookup = orderKeys.ToHashSet();

            result.AddRange(orderRefs.Where(r => lookup.Contains(
                OrderLineAttributionKey.ForOrder(r.ChannelId, r.ExternalOrderId, r.ExternalProductId))));
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task UpsertAsync(IReadOnlyCollection<OrderLineRef> refs, CancellationToken cancellationToken)
    {
        if (refs.Count == 0)
        {
            return;
        }

        List<Guid> ids = refs.Select(r => r.OrderLineId).ToList();

        List<OrderLineRef> existing = await _context.OrderLineRefs
            .Where(r => ids.Contains(r.OrderLineId))
            .ToListAsync(cancellationToken);

        Dictionary<Guid, OrderLineRef> byId = existing.ToDictionary(r => r.OrderLineId);

        foreach (OrderLineRef incoming in refs)
        {
            if (!byId.TryGetValue(incoming.OrderLineId, out OrderLineRef? stored))
            {
                _context.OrderLineRefs.Add(incoming);
                continue;
            }

            stored.OrderId = incoming.OrderId;
            stored.ChannelId = incoming.ChannelId;
            stored.ExternalOrderId = incoming.ExternalOrderId;
            stored.ExternalLineId = incoming.ExternalLineId;
            stored.ExternalProductId = incoming.ExternalProductId;
            stored.ChannelProductId = incoming.ChannelProductId;
            stored.Quantity = incoming.Quantity;
            stored.OrderDate = incoming.OrderDate;
            stored.Currency = incoming.Currency;
            stored.IsCancelled = incoming.IsCancelled;
            stored.UpdatedAt = incoming.UpdatedAt;
        }

        await _context.SaveChangesAsync(cancellationToken);
    }
}
