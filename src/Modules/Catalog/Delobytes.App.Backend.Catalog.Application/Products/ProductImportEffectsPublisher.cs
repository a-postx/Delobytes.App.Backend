using Delobytes.App.Backend.Catalog.Application.Interfaces;
using Delobytes.App.Backend.Catalog.Contracts.Events;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Catalog.Application.Products;

/// <summary>
/// Runs the work an imported batch leaves after its transaction commits: the photos the cards
/// declare and the channel-product links the Sales-side projection needs.
/// </summary>
/// <remarks>
/// Everything here happens after the commit on purpose. Holding a transaction open across
/// marketplace HTTP calls kept a batch busy for seconds, so its completion event reached the
/// aggregator long after the terminal batch had finalised the run. Photos run before the links so
/// that the projection receives the primary photo URL together with the link, instead of learning
/// the link first and the photo never.
/// </remarks>
public class ProductImportEffectsPublisher
{
    /// <summary>
    /// Size variant of the photo the Sales-side projection shows for a product. Matches the
    /// convention used by <c>GetProductsQueryHandler</c> and <c>ProductPhotoService</c>.
    /// </summary>
    private const string ThumbnailSizeVariant = "thumbnail";

    private readonly IProductImportStore _store;
    private readonly IProductPhotoService _photoService;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<ProductImportEffectsPublisher> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProductImportEffectsPublisher"/> class.
    /// </summary>
    /// <param name="store">Persistence port of the import.</param>
    /// <param name="photoService">Photo import service.</param>
    /// <param name="eventPublisher">Event publisher.</param>
    /// <param name="logger">Logger instance.</param>
    public ProductImportEffectsPublisher(
        IProductImportStore store,
        IProductPhotoService photoService,
        IEventPublisher eventPublisher,
        ILogger<ProductImportEffectsPublisher> logger)
    {
        _store = store;
        _photoService = photoService;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    /// <summary>Applies the effects of the cards the batch saved.</summary>
    /// <param name="effects">Effects of the cards that reached the commit.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task PublishAsync(IReadOnlyList<CardEffects> effects, CancellationToken cancellationToken)
    {
        if (effects.Count == 0)
        {
            return;
        }

        await ImportPhotosAsync(effects, cancellationToken);
        await PublishLinksAsync(effects, cancellationToken);
    }

    /// <summary>
    /// Imports the photos collected during the transactional phase. A photo that cannot be
    /// imported is logged and skipped: it must neither fail the batch nor distort its counters,
    /// since the photo service keeps a Failed row so the next import retries it.
    /// </summary>
    private async Task ImportPhotosAsync(IReadOnlyList<CardEffects> effects, CancellationToken cancellationToken)
    {
        foreach (CardEffects effect in effects)
        {
            if (effect.CanonicalPhotoOwner is not Product product)
            {
                continue;
            }

            try
            {
                ProductPhotoImportResult photoResult = await _photoService.ImportCanonicalPhotosAsync(
                    product,
                    effect.CanonicalPhotoSources,
                    cancellationToken);

                LogPhotoResult(effect.ExternalProductId, product.Id, photoResult);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to import canonical photos for ExternalProductId={ExternalProductId}, ProductId={ProductId}",
                    effect.ExternalProductId,
                    product.Id);
            }
        }

        foreach (CardEffects effect in effects)
        {
            if (effect.ChannelPhotoOwner is not ChannelProduct link)
            {
                continue;
            }

            try
            {
                ProductPhotoImportResult photoResult = await _photoService.SyncChannelPhotosAsync(
                    link,
                    effect.ChannelPhotoSources,
                    cancellationToken);

                LogPhotoResult(effect.ExternalProductId, link.ProductId, photoResult);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to import channel photos for ExternalProductId={ExternalProductId}, ProductId={ProductId}",
                    effect.ExternalProductId,
                    link.ProductId);
            }
        }

        // The photo service deliberately does not call SaveChanges: it adds rows to the tracked
        // owner and leaves the transaction boundary to the caller. This is that boundary. A
        // failure here is logged rather than thrown, because the batch is already committed and
        // its counters must still reach the aggregator; the photo service left a Failed or no row
        // behind, so the next import of the same card retries them.
        try
        {
            await _store.SaveChangesAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to persist the photos imported for the batch; the next import retries them.");
        }
    }

    /// <summary>
    /// Publishes a <see cref="ChannelProductLinkedEvent"/> for every link touched by this batch.
    /// </summary>
    /// <remarks>
    /// A publication failure is logged and swallowed. The link itself is already committed, so
    /// failing the batch here would report a database failure that did not happen and would lose
    /// the batch counters; the missing projection row is healed by the next import of the same card,
    /// which re-publishes unconditionally. Publication is not transactional with the database, and
    /// this method does not pretend otherwise.
    /// </remarks>
    private async Task PublishLinksAsync(IReadOnlyList<CardEffects> effects, CancellationToken cancellationToken)
    {
        HashSet<Guid> published = new HashSet<Guid>();

        foreach (CardEffects effect in effects)
        {
            if (effect.LinkToPublish is not ChannelProduct link)
            {
                continue;
            }

            // Reconciliation runs for cards that changed no row, so the same link can appear more
            // than once per batch; the Sales consumer upserts, so a duplicate is only wasted work.
            if (!published.Add(link.Id))
            {
                continue;
            }

            try
            {
                ChannelProductLinkedEvent linkedEvent = new ChannelProductLinkedEvent
                {
                    ChannelProductId = link.Id,
                    ProductId = link.ProductId,
                    ChannelId = link.ChannelId,
                    ExternalProductId = link.ExternalProductId,
                    ExternalSku = link.ExternalSku,
                    Sku = link.Product.Sku,
                    ProductName = link.Product.Name,
                    PhotoUrl = ResolvePrimaryPhotoUrl(link),
                };

                await _eventPublisher.PublishAsync(linkedEvent, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to publish ChannelProductLinkedEvent for ChannelProductId={ChannelProductId}",
                    link.Id);
            }
        }
    }

    /// <summary>
    /// Resolves the public URL of the first uploaded thumbnail the card itself shows. Read after
    /// the transaction, so the outcome reflects the committed rows only.
    /// </summary>
    private string? ResolvePrimaryPhotoUrl(ChannelProduct link)
    {
        ChannelProductPhoto? primaryPhoto = link.Photos
            .Where(photo => photo.Status == ProductPhotoStatus.Uploaded
                            && photo.SizeVariant == ThumbnailSizeVariant)
            .OrderBy(photo => photo.DisplayOrder)
            .FirstOrDefault();

        if (primaryPhoto == null)
        {
            return null;
        }

        return _photoService.GetPublicUrl(primaryPhoto.StorageKey);
    }

    private void LogPhotoResult(string externalProductId, Guid productId, ProductPhotoImportResult photoResult)
    {
        if (photoResult.Failed == 0 && photoResult.Removed == 0)
        {
            return;
        }

        _logger.LogWarning(
            "ExternalProductId={ExternalProductId}, ProductId={ProductId}: {Imported} photo(s) imported, {Replaced} replaced, {Skipped} skipped, {Removed} removed, {Failed} failed",
            externalProductId,
            productId,
            photoResult.Imported,
            photoResult.Replaced,
            photoResult.Skipped,
            photoResult.Removed,
            photoResult.Failed);
    }
}
