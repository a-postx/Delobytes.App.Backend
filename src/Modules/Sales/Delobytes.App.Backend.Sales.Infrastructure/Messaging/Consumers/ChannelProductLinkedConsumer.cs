using Delobytes.App.Backend.Catalog.Contracts.Events;
using Delobytes.App.Backend.Sales.Domain.Entities;
using Delobytes.App.Backend.Sales.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Sales.Infrastructure.Messaging.Consumers;

/// <summary>
/// Consumer for ChannelProductLinkedEvent, published by Catalog after a channel product link has
/// been created or updated. Upserts the Sales-side <see cref="ChannelProductRef"/> projection, which
/// is what <see cref="OrderLine.ChannelProductId"/> is resolved from during an order import.
/// </summary>
public class ChannelProductLinkedConsumer
{
    private readonly SalesDbContext _context;
    private readonly ILogger<ChannelProductLinkedConsumer> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChannelProductLinkedConsumer"/> class.
    /// </summary>
    /// <param name="context">Sales database context.</param>
    /// <param name="logger">Logger instance.</param>
    public ChannelProductLinkedConsumer(
        SalesDbContext context,
        ILogger<ChannelProductLinkedConsumer> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Processes the ChannelProductLinkedEvent message.
    /// </summary>
    /// <param name="message">Event message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ProcessAsync(ChannelProductLinkedEvent message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        DateTimeOffset now = DateTimeOffset.UtcNow;

        ChannelProductRef? projection = await _context.ChannelProductRefs
            .FirstOrDefaultAsync(r => r.ChannelProductId == message.ChannelProductId, cancellationToken);

        if (projection == null)
        {
            await RemoveStaleProjectionAsync(message, cancellationToken);

            projection = new ChannelProductRef
            {
                ChannelProductId = message.ChannelProductId,
            };

            _context.ChannelProductRefs.Add(projection);

            _logger.LogInformation(
                "Created ChannelProductRef projection. ChannelProductId={ChannelProductId}, ChannelId={ChannelId}, ExternalProductId={ExternalProductId}",
                message.ChannelProductId,
                message.ChannelId,
                message.ExternalProductId);
        }

        projection.ProductId = message.ProductId;
        projection.ChannelId = message.ChannelId;
        projection.ExternalProductId = message.ExternalProductId;
        projection.ExternalSku = message.ExternalSku;
        projection.Sku = message.Sku;
        projection.ProductName = message.ProductName;
        projection.PhotoUrl = message.PhotoUrl;
        projection.UpdatedAt = now;

        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Drops a projection row that still occupies the natural key
    /// <c>(TenantId, ChannelId, ExternalProductId)</c> under a different
    /// <c>ChannelProductId</c>, which happens when a product is relinked and the catalogue issues a
    /// new link identity for the same marketplace item.
    /// </summary>
    /// <remarks>
    /// Removed and saved on its own instead of in the same batch as the insert: EF Core gives no
    /// ordering guarantee between a delete and an insert of rows sharing a unique key, and the
    /// insert hitting the index first would fail the batch. Only the collision path pays for the
    /// extra round trip.
    /// </remarks>
    /// <param name="message">Event message carrying the incoming natural key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task RemoveStaleProjectionAsync(
        ChannelProductLinkedEvent message, CancellationToken cancellationToken)
    {
        ChannelProductRef? staleProjection = await _context.ChannelProductRefs
            .FirstOrDefaultAsync(
                r => r.ChannelId == message.ChannelId
                    && r.ExternalProductId == message.ExternalProductId
                    && r.ChannelProductId != message.ChannelProductId,
                cancellationToken);

        if (staleProjection == null)
        {
            return;
        }

        _context.ChannelProductRefs.Remove(staleProjection);

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Replaced stale ChannelProductRef projection. StaleChannelProductId={StaleChannelProductId}, NewChannelProductId={NewChannelProductId}, ChannelId={ChannelId}, ExternalProductId={ExternalProductId}",
            staleProjection.ChannelProductId,
            message.ChannelProductId,
            message.ChannelId,
            message.ExternalProductId);
    }
}
