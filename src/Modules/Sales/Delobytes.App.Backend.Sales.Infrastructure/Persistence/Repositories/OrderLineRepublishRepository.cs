using Delobytes.App.Backend.Sales.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Sales.Application.Models;
using Delobytes.App.Backend.Sales.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Delobytes.App.Backend.Sales.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IOrderLineRepublishRepository"/>.
/// </summary>
/// <remarks>
/// The keyset is the order line identifier compared as a <see cref="Guid"/>: Npgsql translates the
/// comparison into a <c>uuid</c> predicate, so the page is read in index order. The rows are
/// projected in the same query — the caller needs no entities, only the facts of the event.
/// </remarks>
public class OrderLineRepublishRepository : IOrderLineRepublishRepository
{
    private readonly SalesDbContext _context;

    /// <summary>
    /// Initializes a new instance of the <see cref="OrderLineRepublishRepository"/> class.
    /// </summary>
    /// <param name="context">Sales database context.</param>
    public OrderLineRepublishRepository(SalesDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<RepublishOrderLine>> GetPageAsync(
        Guid? channelId,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtcExclusive,
        string? afterToken,
        int limit,
        CancellationToken cancellationToken)
    {
        Guid? afterId = ParseCursor(afterToken);

        return await BuildPageQuery(channelId, fromUtc, toUtcExclusive, afterId)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Builds one page: the tenant filters, the optional narrowing, the keyset and the projection.
    /// Internal so tests can assert the SQL EF Core produces for the join and the cursor.
    /// </summary>
    internal IQueryable<RepublishOrderLine> BuildPageQuery(
        Guid? channelId,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtcExclusive,
        Guid? afterId)
    {
        IQueryable<OrderLine> lines = _context.OrderLines.AsNoTracking();

        if (channelId.HasValue)
        {
            Guid channel = channelId.Value;
            lines = lines.Where(line => line.Order.ChannelId == channel);
        }

        if (fromUtc.HasValue)
        {
            DateTimeOffset from = fromUtc.Value;
            lines = lines.Where(line => line.Order.OrderDate >= from);
        }

        if (toUtcExclusive.HasValue)
        {
            DateTimeOffset to = toUtcExclusive.Value;
            lines = lines.Where(line => line.Order.OrderDate < to);
        }

        if (afterId.HasValue)
        {
            Guid cursor = afterId.Value;
            lines = lines.Where(line => line.Id.CompareTo(cursor) > 0);
        }

        return lines
            .OrderBy(line => line.Id)
            .Select(line => new RepublishOrderLine
            {
                OrderLineId = line.Id,
                OrderId = line.OrderId,
                ChannelId = line.Order.ChannelId,
                ExternalOrderId = line.Order.ExternalOrderId,
                ExternalLineId = line.ExternalLineId,
                ExternalProductId = line.ExternalProductId,
                ChannelProductId = line.ChannelProductId,
                Quantity = line.Quantity,
                OrderDate = line.Order.OrderDate,
                Currency = line.Currency,
                IsCancelled = line.IsCancelled,
            });
    }

    /// <summary>Parses the continuation token; a malformed token is a caller error, not page one.</summary>
    internal static Guid? ParseCursor(string? afterToken)
    {
        if (string.IsNullOrWhiteSpace(afterToken))
        {
            return null;
        }

        if (!Guid.TryParse(afterToken, out Guid cursor))
        {
            throw new ArgumentException(
                "The continuation token must be an order line identifier produced by the previous page.",
                nameof(afterToken));
        }

        return cursor;
    }
}
