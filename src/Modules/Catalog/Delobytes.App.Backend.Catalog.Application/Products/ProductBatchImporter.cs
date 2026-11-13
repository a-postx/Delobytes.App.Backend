using Delobytes.App.Backend.Catalog.Application.Interfaces;
using Delobytes.App.Backend.Catalog.Application.Numbering;
using Delobytes.App.Backend.Catalog.Application.Packing;
using Delobytes.App.Backend.Catalog.Contracts.Events;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using Delobytes.App.Backend.Contracts.Errors;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using Delobytes.App.Backend.Integrations.Contracts.Models;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Catalog.Application.Products;

/// <summary>
/// Imports one batch of marketplace product cards into the catalog: resolves each card to a known
/// product (channel link, then barcode, then a new product), synchronises barcodes and packing unit,
/// queues photos and channel links for after the commit, and reports batch counters.
/// </summary>
public class ProductBatchImporter
{
    /// <summary>
    /// Mirrors ProductConfiguration: Product.Description is varchar(20000). WB allows
    /// descriptions up to 5000 characters, so a longer card would otherwise be rejected by
    /// PostgreSQL at SaveChanges and roll back the whole batch.
    /// </summary>
    private const int DescriptionMaxLength = 20000;
    private const int NameMaxLength = 2000;

    /// <summary>
    /// How many codes a card may burn while looking for one that is also free as a SKU. Each
    /// discarded number is a gap in the product numbering, which is permitted.
    /// </summary>
    private const int MaxCodeAttempts = 5;

    /// <summary>
    /// Size variant of the photo the Sales-side projection shows for a product. Matches the
    /// convention used by <c>GetProductsQueryHandler</c> and <c>ProductPhotoService</c>.
    /// </summary>
    private const string ThumbnailSizeVariant = "thumbnail";

    private readonly IProductImportStore _store;
    private readonly IProductPhotoService _photoService;
    private readonly IEventPublisher _eventPublisher;
    private readonly INumberingSchemeProvider _numberingSchemes;
    private readonly INumberAllocator _numberAllocator;
    private readonly ILogger<ProductBatchImporter> _logger;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProductBatchImporter"/> class.
    /// </summary>
    /// <param name="store">Persistence port of the import.</param>
    /// <param name="photoService">Photo import/delete service.</param>
    /// <param name="eventPublisher">Event publisher.</param>
    /// <param name="numberingSchemes">Resolves the scheme new product codes are issued from.</param>
    /// <param name="numberAllocator">
    /// Issues new product codes. It runs inside the batch transaction the store owns, so the
    /// numbers stay durable together with the products they belong to.
    /// </param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="timeProvider">Clock source.</param>
    public ProductBatchImporter(
        IProductImportStore store,
        IProductPhotoService photoService,
        IEventPublisher eventPublisher,
        INumberingSchemeProvider numberingSchemes,
        INumberAllocator numberAllocator,
        ILogger<ProductBatchImporter> logger,
        TimeProvider timeProvider)
    {
        _store = store;
        _photoService = photoService;
        _eventPublisher = eventPublisher;
        _numberingSchemes = numberingSchemes;
        _numberAllocator = numberAllocator;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Processes the ProductBatchImportRequestedEvent message.
    /// </summary>
    /// <param name="message">Event message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ImportAsync(ProductBatchImportRequestedEvent message, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "ProductBatchImporter started processing batch. JobRunId={JobRunId}, ConnectionId={ConnectionId}, ChannelId={ChannelId}, CardsCount={CardsCount}, IsLastBatch={IsLastBatch}",
            message.JobRunId,
            message.ConnectionId,
            message.ChannelId,
            message.Cards.Count,
            message.IsLastBatch);

        int recordsProcessed = 0;
        int recordsCreated = 0;
        int recordsUpdated = 0;
        int recordsSkipped = 0;
        int recordsFailed = 0;
        List<string> errors = new List<string>();

        // Photos are downloaded only after the database transaction has committed: holding a
        // transaction open across marketplace HTTP calls kept this batch busy for seconds, so
        // its ProductBatchImportCompletedEvent reached the aggregator long after the terminal
        // batch had already finalised the job.
        List<PendingPhotoImport> pendingPhotoImports = new List<PendingPhotoImport>();

        // Channel-product links observed during the transactional phase. Published only after the
        // commit, because the Sales-side projection must never learn about a link that was rolled
        // back, and because a message published inside the transaction can reach the Sales consumer
        // before the row is visible to it.
        List<PendingChannelProductLink> pendingChannelProductLinks = new List<PendingChannelProductLink>();

        using (IProductImportBatchTransaction transaction = await _store.BeginBatchAsync(cancellationToken))
        {
            try
            {
                Channel? channel = await _store.FindChannelAsync(message.ChannelId, cancellationToken);

                if (channel == null)
                {
                    string channelError = $"Channel with Id={message.ChannelId} not found.";
                    _logger.LogError(channelError);
                    errors.Add(channelError);
                    recordsFailed = message.Cards.Count;
                }
                else
                {
                    foreach (ChannelProductCard card in message.Cards)
                    {
                        using (IProductImportCardScope cardScope = await transaction.BeginCardAsync(cancellationToken))
                        {
                            try
                            {
                                ImportResult result = await ProcessCardAsync(card, message.ChannelId, pendingPhotoImports, pendingChannelProductLinks, cancellationToken);

                                // Now one flush per card instead of one per batch.
                                await cardScope.SaveAsync(cancellationToken);

                                recordsProcessed++;

                                switch (result.Status)
                                {
                                    case ImportStatus.Created:
                                        recordsCreated++;
                                        break;
                                    case ImportStatus.Updated:
                                        recordsUpdated++;
                                        break;
                                    case ImportStatus.Skipped:
                                        recordsSkipped++;
                                        break;
                                    case ImportStatus.Failed:
                                        recordsFailed++;
                                        if (!string.IsNullOrEmpty(result.ErrorMessage))
                                        {
                                            errors.Add($"NmId={card.ExternalProductId}: {result.ErrorMessage}");
                                        }

                                        break;
                                }
                            }
                            catch (Exception ex)
                            {
                                // Back to the state before this card, both in PostgreSQL and in the change tracker.
                                await cardScope.RollbackAsync(cancellationToken);

                                recordsProcessed++;
                                recordsFailed++;
                                errors.Add($"NmId={card.ExternalProductId}: {ex.Message}");
                                _logger.LogError(ex, "Failed to process card NmId={NmId}", card.ExternalProductId);
                            }
                        }
                    }
                }

                // The full-set reconciliation is NOT done here: this batch cannot see its siblings'
                // uncommitted rows, so it would read every card they have not yet committed as
                // dropped by the channel. Integrations publishes
                // ChannelProductFullSetReconciliationRequestedEvent once every batch of the run has
                // been recorded, and Catalog sweeps then.
                await transaction.CommitAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(cancellationToken);
                _logger.LogError(ex, "Failed to process batch. JobRunId={JobRunId}", message.JobRunId);

                ProductBatchImportCompletedEvent rollbackEvent = new ProductBatchImportCompletedEvent
                {
                    JobRunId = message.JobRunId,
                    RecordsProcessed = 0,
                    RecordsCreated = 0,
                    RecordsUpdated = 0,
                    RecordsSkipped = 0,
                    RecordsFailed = message.Cards.Count,
                    ErrorMessage = $"Batch processing failed: {ex.Message}",
                    IsLastBatch = message.IsLastBatch,
                    TotalBatches = message.TotalBatches,
                };

                await _eventPublisher.PublishAsync(rollbackEvent, cancellationToken);
                throw;
            }
        }

        // The transaction is closed: products, channel products, barcodes and packing units are
        // durable. Photos are downloaded and uploaded now, outside any database transaction.
        await ImportPhotosAsync(pendingPhotoImports, cancellationToken);

        // Published after the photos so the Sales-side projection receives the primary photo URL
        // together with the link, instead of learning the link first and the photo never.
        await PublishChannelProductLinksAsync(pendingChannelProductLinks, cancellationToken);

        string? errorMessage = errors.Count > 0 ? string.Join("; ", errors) : null;

        ProductBatchImportCompletedEvent completedEvent = new ProductBatchImportCompletedEvent
        {
            JobRunId = message.JobRunId,
            RecordsProcessed = recordsProcessed,
            RecordsCreated = recordsCreated,
            RecordsUpdated = recordsUpdated,
            RecordsSkipped = recordsSkipped,
            RecordsFailed = recordsFailed,
            ErrorMessage = errorMessage,
            IsLastBatch = message.IsLastBatch,
            TotalBatches = message.TotalBatches,
        };

        await _eventPublisher.PublishAsync(completedEvent, cancellationToken);

        _logger.LogInformation(
            "ProductBatchImporter completed batch. JobRunId={JobRunId}, Processed={Processed}, Created={Created}, Updated={Updated}, Skipped={Skipped}, Failed={Failed}",
            message.JobRunId,
            recordsProcessed,
            recordsCreated,
            recordsUpdated,
            recordsSkipped,
            recordsFailed);
    }

    /// <summary>
    /// Imports the photos collected during the transactional phase. A photo that cannot be
    /// imported is logged and skipped: it must neither fail the batch nor distort its counters,
    /// since <see cref="IProductPhotoService.ImportPhotosAsync"/> keeps a Failed row so the next
    /// import retries it.
    /// </summary>
    private async Task ImportPhotosAsync(
        List<PendingPhotoImport> pendingPhotoImports,
        CancellationToken cancellationToken)
    {
        if (pendingPhotoImports.Count == 0)
        {
            return;
        }

        foreach (PendingPhotoImport pending in pendingPhotoImports)
        {
            try
            {
                ProductPhotoImportResult photoResult = await _photoService.ImportPhotosAsync(
                    pending.Product,
                    pending.ChannelId,
                    pending.Sources,
                    cancellationToken);

                if (photoResult.Failed > 0 || photoResult.Removed > 0)
                {
                    _logger.LogWarning(
                        "ExternalProductId={ExternalProductId}, ChannelId={ChannelId}: {Imported} photo(s) imported, {Replaced} replaced, {Skipped} skipped, {Removed} removed, {Failed} failed",
                        pending.ExternalProductId,
                        pending.ChannelId,
                        photoResult.Imported,
                        photoResult.Replaced,
                        photoResult.Skipped,
                        photoResult.Removed,
                        photoResult.Failed);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to import photos for ExternalProductId={ExternalProductId}, ProductId={ProductId}",
                    pending.ExternalProductId,
                    pending.Product.Id);
            }
        }

        // Test/design note: the photo service deliberately does not call SaveChanges Р Р†Р вЂљРІР‚Сњ
        // it adds ProductPhoto rows to the tracked product and leaves the transaction boundary
        // to the caller. This is that boundary.
        await _store.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Resolves a card to a product in two steps: the channel link by nmID first, then an existing
    /// product by barcode. A card that matches neither is imported as a new product.
    /// </summary>
    private async Task<ImportResult> ProcessCardAsync(
        ChannelProductCard card,
        Guid channelId,
        List<PendingPhotoImport> pendingPhotoImports,
        List<PendingChannelProductLink> pendingChannelProductLinks,
        CancellationToken cancellationToken)
    {
        string externalProductId = card.ExternalProductId;

        ChannelProduct? existingChannelProduct = await _store
            .FindChannelProductWithProductAsync(channelId, externalProductId, cancellationToken);

        if (existingChannelProduct != null)
        {
            return await UpdateExistingProductAsync(existingChannelProduct, card, pendingPhotoImports, pendingChannelProductLinks, cancellationToken);
        }

        Product? matchedProduct = await FindProductByBarcodeAsync(card, cancellationToken);

        if (matchedProduct != null)
        {
            return await LinkExistingProductAsync(matchedProduct, card, channelId, pendingPhotoImports, pendingChannelProductLinks, cancellationToken);
        }

        return await CreateNewProductAsync(card, channelId, pendingPhotoImports, pendingChannelProductLinks, cancellationToken);
    }

    /// <summary>
    /// Finds a product already known to the catalog whose barcode appears on the marketplace card.
    /// This is the only automatic matching rule besides the nmID link: Product.Sku is deliberately
    /// not consulted, so renaming the internal SKU can never re-route or duplicate an import.
    /// Returns null when the card carries no barcodes or when the match is ambiguous.
    /// </summary>
    private async Task<Product?> FindProductByBarcodeAsync(
        ChannelProductCard card,
        CancellationToken cancellationToken)
    {
        if (card.Barcodes.Count == 0)
        {
            return null;
        }

        IReadOnlyList<Product> productsByBarcode = await _store
            .FindProductsByBarcodesAsync(card.Barcodes, cancellationToken);

        if (productsByBarcode.Count == 1)
        {
            return productsByBarcode[0];
        }

        // Ambiguous: several catalog products share a barcode with this card. Linking to an
        // arbitrary one would silently attach an order history to the wrong product, so the card
        // is left unmatched and falls through to import as a new product.
        if (productsByBarcode.Count > 1)
        {
            _logger.LogWarning(
                "Multiple products found with barcodes matching NmId={NmId}. Skipping automatic matching.",
                card.ExternalProductId);
        }

        return null;
    }

    private async Task<ImportResult> UpdateExistingProductAsync(
        ChannelProduct channelProduct,
        ChannelProductCard card,
        List<PendingPhotoImport> pendingPhotoImports,
        List<PendingChannelProductLink> pendingChannelProductLinks,
        CancellationToken cancellationToken)
    {
        // Temporary measure: Product is the source of truth, but until marketplace write-back
        // exists, import keeps overwriting Name/Description/Barcodes/PackingUnit for linked
        // products. The frontend disables editing these fields whenever the product has a
        // ChannelProduct link (see ProductChannelLinkDto). Once write-back ships, import must
        // stop overwriting these fields and the frontend restriction can be lifted.
        bool hasChanges = false;

        if (channelProduct.Product.Name != card.Name)
        {
            channelProduct.Product.Name = TrimName(card.Name);
            hasChanges = true;
        }

        if (channelProduct.Product.Description != card.Description)
        {
            channelProduct.Product.Description = TrimDescription(card.Description);
            hasChanges = true;
        }

        if (channelProduct.ExternalSku != (card.OfferId ?? string.Empty))
        {
            channelProduct.ExternalSku = (card.OfferId ?? string.Empty);
            hasChanges = true;
        }

        if (channelProduct.ChannelSpecificData != card.ChannelSpecificData)
        {
            channelProduct.ChannelSpecificData = card.ChannelSpecificData;
            hasChanges = true;
        }

        // Reactivation is unconditional: the card is in the listing, so the link is live again,
        // regardless of what the previous run concluded.
        if (!channelProduct.IsActive)
        {
            channelProduct.IsActive = true;
            hasChanges = true;
        }

        bool barcodesChanged = await SynchronizeBarcodesAsync(
            channelProduct.Product,
            card.Barcodes,
            ResolveBarcodeType(card),
            cancellationToken);

        if (barcodesChanged)
        {
            hasChanges = true;
        }

        bool packingUnitChanged = SynchronizePackingUnit(channelProduct.Product, card, _timeProvider.GetUtcNow());

        if (packingUnitChanged)
        {
            hasChanges = true;
        }

        QueuePhotos(channelProduct.Product, card, channelProduct.ChannelId, pendingPhotoImports);

        // Correctness of the Sales-side projection must not depend on whether this card happened to
        // change anything: a link that was created before this event existed, or one whose earlier
        // publication failed, is healed by the next import of the same card.
        QueueChannelProductLink(channelProduct, pendingChannelProductLinks);

        channelProduct.LastSyncedAt = _timeProvider.GetUtcNow();

        return hasChanges
            ? new ImportResult { Status = ImportStatus.Updated }
            : new ImportResult { Status = ImportStatus.Skipped };
    }

    /// <summary>
    /// Links a product found by barcode to the importing channel, for the case where the card's
    /// nmID is not yet known. A product may hold only one link per channel (unique index on
    /// ProductId + ChannelId), so when a link already exists the card is skipped with a warning
    /// rather than a second link created: creating one would violate the index and roll back the
    /// whole batch, and overwriting the existing link would make its data flip between the two
    /// nmID each time either card is imported.
    /// </summary>
    private async Task<ImportResult> LinkExistingProductAsync(
        Product product,
        ChannelProductCard card,
        Guid channelId,
        List<PendingPhotoImport> pendingPhotoImports,
        List<PendingChannelProductLink> pendingChannelProductLinks,
        CancellationToken cancellationToken)
    {
        ChannelProduct? existingLink = await _store
            .FindChannelProductLinkAsync(product.Id, channelId, cancellationToken);

        if (existingLink != null)
        {
            _logger.LogWarning(
                "NmId={NmId} matched ProductId={ProductId} by barcode, but that product is already linked to ChannelId={ChannelId} via nmID={LinkedNmId}. Skipping the card: one product can hold only one link per channel.",
                card.ExternalProductId,
                product.Id,
                channelId,
                existingLink.ExternalProductId);

            return new ImportResult { Status = ImportStatus.Skipped };
        }

        ChannelProduct channelProduct = new ChannelProduct
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            ChannelId = channelId,
            ExternalProductId = card.ExternalProductId,
            ExternalSku = (card.OfferId ?? string.Empty),
            ChannelSpecificData = card.ChannelSpecificData,
            IsActive = true,
            LastSyncedAt = _timeProvider.GetUtcNow(),
            CreatedAt = _timeProvider.GetUtcNow()
        };

        _store.AddChannelProduct(channelProduct);

        product.Name = TrimName(card.Name);
        product.Description = TrimDescription(card.Description);

        await SynchronizeBarcodesAsync(product, card.Barcodes, ResolveBarcodeType(card), cancellationToken);
        SynchronizePackingUnit(product, card, _timeProvider.GetUtcNow());

        QueuePhotos(product, card, channelId, pendingPhotoImports);

        QueueChannelProductLink(channelProduct, pendingChannelProductLinks);

        return new ImportResult { Status = ImportStatus.Updated };
    }

    private async Task<ImportResult> CreateNewProductAsync(
        ChannelProductCard card,
        Guid channelId,
        List<PendingPhotoImport> pendingPhotoImports,
        List<PendingChannelProductLink> pendingChannelProductLinks,
        CancellationToken cancellationToken)
    {
        // Product.Sku is unique per tenant and is copied from the marketplace vendor code when
        // that value is free. It is never a matching key: a vendor code another product already
        // holds — typically a hand-made product with no channel link — must not abort the card,
        // so the product falls back to its own code, which is unique by construction unless a user
        // typed that code in by hand before numbering existed.
        (string code, string sku) = await ResolveCodeAndSkuAsync(card.OfferId, cancellationToken);

        Product product = new Product
        {
            Id = Guid.NewGuid(),
            Code = code,
            Sku = sku,
            Name = TrimName(card.Name),
            Description = TrimDescription(card.Description),
            Status = ProductStatus.Active,
            CreationSource = ResolveCreationSource(card),
            CreatedAt = _timeProvider.GetUtcNow()
        };

        _store.AddProduct(product);

        ChannelProduct channelProduct = new ChannelProduct
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            ChannelId = channelId,
            ExternalProductId = card.ExternalProductId,
            ExternalSku = (card.OfferId ?? string.Empty),
            ChannelSpecificData = card.ChannelSpecificData,
            IsActive = true,
            LastSyncedAt = _timeProvider.GetUtcNow(),
            CreatedAt = _timeProvider.GetUtcNow()
        };

        _store.AddChannelProduct(channelProduct);

        await SynchronizeBarcodesAsync(product, card.Barcodes, ResolveBarcodeType(card), cancellationToken);
        SynchronizePackingUnit(product, card, _timeProvider.GetUtcNow());

        QueuePhotos(product, card, channelId, pendingPhotoImports);

        QueueChannelProductLink(channelProduct, pendingChannelProductLinks);

        return new ImportResult { Status = ImportStatus.Created };
    }

    /// <summary>
    /// Issues the immutable product code and picks the internal SKU for a card that becomes a new
    /// product: the trimmed vendor code when it is free, otherwise the code itself.
    /// </summary>
    /// <remarks>
    /// A code used as a SKU can still collide, because Product.Sku is user-editable and a number
    /// typed in by hand may equal a code the allocator has not issued yet. Such a collision burns
    /// the number and moves to the next one; five of them mean the catalogue cannot be told apart
    /// from the numbering scheme, and the card fails instead of guessing.
    /// </remarks>
    private async Task<(string Code, string Sku)> ResolveCodeAndSkuAsync(
        string? offerId,
        CancellationToken cancellationToken)
    {
        string candidate = (offerId ?? string.Empty).Trim();
        string code = await AllocateCodeAsync(cancellationToken);

        if (candidate.Length > 0 && !await _store.IsSkuTakenAsync(candidate, cancellationToken))
        {
            return (code, candidate);
        }

        for (int attempt = 1; attempt <= MaxCodeAttempts; attempt++)
        {
            if (!await _store.IsSkuTakenAsync(code, cancellationToken))
            {
                return (code, code);
            }

            if (attempt == MaxCodeAttempts)
            {
                break;
            }

            _logger.LogWarning(
                "Code {Code} is already used as a SKU by another product in this tenant. Allocating the next code.",
                code);

            code = await AllocateCodeAsync(cancellationToken);
        }

        throw new AppException(ErrorCodes.Catalog.ProductCodeAllocationFailed);
    }

    private async Task<string> AllocateCodeAsync(CancellationToken cancellationToken)
    {
        NumberingScheme scheme = await _numberingSchemes.GetDefaultProductSchemeAsync(cancellationToken);
        return await _numberAllocator.AllocateAsync(scheme.Id, cancellationToken);
    }

    /// <summary>
    /// Records a channel-product link for publication after the transaction commits. The whole
    /// <see cref="ChannelProduct"/> is kept rather than a copy of its fields, so the event is built
    /// from the values the database actually holds once the commit succeeds.
    /// </summary>
    private static void QueueChannelProductLink(
        ChannelProduct channelProduct,
        List<PendingChannelProductLink> pendingChannelProductLinks)
    {
        // Reconciliation now runs for cards that changed no row, so the same link can be queued
        // more than once per batch; the Sales consumer upserts, so a duplicate is only wasted work.
        bool alreadyQueued = pendingChannelProductLinks
            .Any(p => p.ChannelProduct.Id == channelProduct.Id);

        if (alreadyQueued)
        {
            return;
        }

        pendingChannelProductLinks.Add(new PendingChannelProductLink
        {
            ChannelProduct = channelProduct,
        });
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
    private async Task PublishChannelProductLinksAsync(
        List<PendingChannelProductLink> pendingChannelProductLinks,
        CancellationToken cancellationToken)
    {
        if (pendingChannelProductLinks.Count == 0)
        {
            return;
        }

        foreach (PendingChannelProductLink pending in pendingChannelProductLinks)
        {
            try
            {
                ChannelProduct channelProduct = pending.ChannelProduct;

                ChannelProductLinkedEvent linkedEvent = new ChannelProductLinkedEvent
                {
                    ChannelProductId = channelProduct.Id,
                    ProductId = channelProduct.ProductId,
                    ChannelId = channelProduct.ChannelId,
                    ExternalProductId = channelProduct.ExternalProductId,
                    ExternalSku = channelProduct.ExternalSku,
                    Sku = channelProduct.Product.Sku,
                    ProductName = channelProduct.Product.Name,
                    PhotoUrl = ResolvePrimaryPhotoUrl(channelProduct.Product, channelProduct.ChannelId),
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
                    pending.ChannelProduct.Id);
            }
        }
    }

    /// <summary>
    /// Resolves the public URL of the first uploaded thumbnail that the given channel's card shows.
    /// Read after the transaction, so the outcome reflects the committed rows only.
    /// Legacy rows with <see cref="Guid.Empty"/> as their channel are excluded: their owner is
    /// unknown, and the photo import of that card assigns it before this method runs.
    /// </summary>
    private string? ResolvePrimaryPhotoUrl(Product product, Guid channelId)
    {
        ProductPhoto? primaryPhoto = product.Photos
            .Where(p => p.Status == ProductPhotoStatus.Uploaded
                        && p.SizeVariant == ThumbnailSizeVariant
                        && p.ChannelId == channelId)
            .OrderBy(p => p.DisplayOrder)
            .FirstOrDefault();

        if (primaryPhoto == null)
        {
            return null;
        }

        return _photoService.GetPublicUrl(primaryPhoto);
    }

    /// <summary>
    /// Records the photos of a card for download after the database transaction commits. The
    /// tracked <see cref="Product"/> instance is kept, so the rows the photo service adds land in
    /// the same context that owns the product. An empty source list is queued as well: it is how a
    /// card whose photos were all removed in the marketplace reaches reconciliation.
    /// </summary>
    private static void QueuePhotos(
        Product product,
        ChannelProductCard card,
        Guid channelId,
        List<PendingPhotoImport> pendingPhotoImports)
    {
        List<MarketplacePhotoSource> photoSources = BuildPhotoSources(card);

        pendingPhotoImports.Add(new PendingPhotoImport
        {
            Product = product,
            ChannelId = channelId,
            Sources = photoSources,
            ExternalProductId = card.ExternalProductId,
        });
    }

    /// <summary>
    /// Projects the card's photo variants onto the importer's sources. Display order and variant
    /// names come from the channel mapper, so this stays independent of one marketplace's sizes.
    /// </summary>
    private static List<MarketplacePhotoSource> BuildPhotoSources(ChannelProductCard card)
    {
        List<MarketplacePhotoSource> sources = new List<MarketplacePhotoSource>(card.Photos.Count);

        foreach (ChannelProductPhoto photo in card.Photos)
        {
            if (string.IsNullOrEmpty(photo.Url))
            {
                continue;
            }

            sources.Add(new MarketplacePhotoSource
            {
                Url = photo.Url,
                ExternalId = $"{card.ExternalProductId}_{photo.DisplayOrder}_{photo.SizeVariant}",
                DisplayOrder = photo.DisplayOrder,
                SizeVariant = photo.SizeVariant,
                Width = photo.Width,
                Height = photo.Height,
                Source = ResolvePhotoSource(card)
            });
        }

        return sources;
    }

    private static string ResolveBarcodeType(ChannelProductCard card)
    {
        return card.BarcodeType ?? card.ChannelCode;
    }

    private static string ResolvePhotoSource(ChannelProductCard card)
    {
        return card.ChannelCode == ChannelCodes.Wildberries
            ? MarketplacePhotoSources.Wildberries
            : card.ChannelCode;
    }

    private static CreationSource ResolveCreationSource(ChannelProductCard card)
    {
        return card.ChannelCode == ChannelCodes.Wildberries
            ? CreationSource.WildberriesImport
            : CreationSource.Manual;
    }

    /// <summary>
    /// Creates or updates the product's canonical packing unit from marketplace-reported
    /// dimensions. Length/width/height are required on PackingUnit, so a card without all three
    /// leaves any existing unit untouched. A channel row is never selected here: those describe
    /// what the marketplace reports and are written separately.
    /// </summary>
    private static bool SynchronizePackingUnit(Product product, ChannelProductCard card, DateTimeOffset now)
    {
        if (card.LengthCm == null || card.WidthCm == null || card.HeightCm == null)
        {
            return false;
        }

        PackingUnit? existing = PackingUnitResolver.ResolveCanonical(product);

        if (existing == null)
        {
            product.PackingUnits.Add(new PackingUnit
            {
                Id = Guid.NewGuid(),
                ProductId = product.Id,
                LengthCm = card.LengthCm.Value,
                WidthCm = card.WidthCm.Value,
                HeightCm = card.HeightCm.Value,
                WeightKg = card.WeightKg,
                IsActive = true,
                CreatedAt = now
            });

            return true;
        }

        bool hasChanges = false;

        if (existing.LengthCm != card.LengthCm.Value)
        {
            existing.LengthCm = card.LengthCm.Value;
            hasChanges = true;
        }

        if (existing.WidthCm != card.WidthCm.Value)
        {
            existing.WidthCm = card.WidthCm.Value;
            hasChanges = true;
        }

        if (existing.HeightCm != card.HeightCm.Value)
        {
            existing.HeightCm = card.HeightCm.Value;
            hasChanges = true;
        }

        if (existing.WeightKg != card.WeightKg)
        {
            existing.WeightKg = card.WeightKg;
            hasChanges = true;
        }

        if (hasChanges)
        {
            existing.UpdatedAt = now;
        }

        return hasChanges;
    }

    private async Task<bool> SynchronizeBarcodesAsync(
        Product product,
        List<string> newBarcodes,
        string barcodeType,
        CancellationToken cancellationToken)
    {
        if (newBarcodes.Count == 0)
        {
            return false;
        }

        bool hasChanges = false;

        List<ProductBarcode> existingBarcodes = product.Barcodes.ToList();
        HashSet<string> existingBarcodeValues = existingBarcodes
            .Where(b => b.Type == barcodeType)
            .Select(b => b.Value)
            .ToHashSet();

        foreach (string barcodeValue in newBarcodes)
        {
            if (!existingBarcodeValues.Contains(barcodeValue))
            {
                ProductBarcode barcode = new ProductBarcode
                {
                    Id = Guid.NewGuid(),
                    ProductId = product.Id,
                    Value = barcodeValue,
                    Type = barcodeType,
                    IsDefault = false,
                    CreatedAt = _timeProvider.GetUtcNow()
                };

                _store.AddProductBarcode(barcode);
                hasChanges = true;
            }
        }

        return hasChanges;
    }

    private static string TrimName(string name)
    {
        if (string.IsNullOrEmpty(name) || name.Length <= NameMaxLength)
        {
            return name;
        }

        return name[..NameMaxLength];
    }

    private static string? TrimDescription(string? description)
    {
        if (string.IsNullOrEmpty(description) || description.Length <= DescriptionMaxLength)
        {
            return description;
        }

        return description[..DescriptionMaxLength];
    }

    /// <summary>
    /// A channel-product link whose <see cref="ChannelProductLinkedEvent"/> still has to be
    /// published, once the transaction that created or updated it has committed.
    /// </summary>
    private class PendingChannelProductLink
    {
        public ChannelProduct ChannelProduct { get; set; } = default!;
    }

    /// <summary>
    /// A product whose photos still have to be downloaded once the transaction has committed.
    /// </summary>
    private class PendingPhotoImport
    {
        public Product Product { get; set; } = default!;

        public Guid ChannelId { get; set; }

        public List<MarketplacePhotoSource> Sources { get; set; } = new();

        public string ExternalProductId { get; set; } = default!;
    }

    private class ImportResult
    {
        public ImportStatus Status { get; set; }
        public string? ErrorMessage { get; set; }
    }

    private enum ImportStatus
    {
        Created,
        Updated,
        Skipped,
        Failed
    }
}
