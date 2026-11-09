using Delobytes.App.Backend.Catalog.Contracts.Events;
using Delobytes.App.Backend.Sales.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Sales.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Sales.Application.Services;

/// <summary>
/// Handles ChannelProductLinkedEvent, published by Catalog after a channel product link has been
/// created or updated. Upserts the Sales-side <see cref="ChannelProductRef"/> projection, which is
/// what <see cref="OrderLine.ChannelProductId"/> is resolved from during an order import.
/// </summary>
public class ChannelProductLinker
{
    private readonly IChannelProductRefRepository _repository;
    private readonly ILogger<ChannelProductLinker> _logger;
    private readonly TimeProvider _timeProvider;

    public ChannelProductLinker(
        IChannelProductRefRepository repository,
        ILogger<ChannelProductLinker> logger,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public async Task UpdateChannelProductRefAsync(ChannelProductLinkedEvent message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        DateTimeOffset now = _timeProvider.GetUtcNow();

        ChannelProductRef? projection = await _repository
            .FindByChannelProductIdAsync(message.ChannelProductId, cancellationToken);

        if (projection == null)
        {
            await RemoveStaleProjectionAsync(message, cancellationToken);

            projection = new ChannelProductRef
            {
                ChannelProductId = message.ChannelProductId,
            };

            _repository.Add(projection);

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

        await _repository.SaveChangesAsync(cancellationToken);
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
        ChannelProductRef? staleProjection = await _repository.FindStaleAsync(
            message.ChannelId,
            message.ExternalProductId,
            message.ChannelProductId,
            cancellationToken);

        if (staleProjection == null)
        {
            return;
        }

        _repository.Remove(staleProjection);

        await _repository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Replaced stale ChannelProductRef projection. StaleChannelProductId={StaleChannelProductId}, NewChannelProductId={NewChannelProductId}, ChannelId={ChannelId}, ExternalProductId={ExternalProductId}",
            staleProjection.ChannelProductId,
            message.ChannelProductId,
            message.ChannelId,
            message.ExternalProductId);
    }
}
