using Delobytes.App.Backend.Catalog.Application.Interfaces;
using Delobytes.App.Backend.Catalog.Application.Numbering;
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
    /// Resolves a card to a product: the existing channel link first, then a match by barcode, then
    /// a match by the seller article the card carries on any channel. A card that matches nothing
    /// is imported as a new product; an ambiguous match is skipped instead.
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

        // A card with neither barcode nor seller article carries nothing that could identify it.
        // Importing it would create a product per run, so it is treated as a draft of the channel
        // and left out of the catalogue until the channel publishes identifiers.
        if (card.Barcodes.Count == 0 && string.IsNullOrWhiteSpace(card.OfferId))
        {
            _logger.LogInformation(
                "ChannelId={ChannelId}, ExternalProductId={ExternalProductId}: card has neither barcode nor offer id; treating as draft and skipping.",
                channelId,
                externalProductId);

            return new ImportResult { Status = ImportStatus.Skipped };
        }

        ProductMatch match = await FindProductAsync(card, channelId, cancellationToken);

        if (match.IsAmbiguous)
        {
            return new ImportResult { Status = ImportStatus.Skipped };
        }

        if (match.Product != null)
        {
            return await LinkExistingProductAsync(match.Product, card, channelId, pendingPhotoImports, pendingChannelProductLinks, cancellationToken);
        }

        return await CreateNewProductAsync(card, channelId, pendingPhotoImports, pendingChannelProductLinks, cancellationToken);
    }

    /// <summary>
    /// Finds the product a new card belongs to: by barcode first, then by the seller article the
    /// card carries (<c>vendorCode</c> / <c>offer_id</c>) on any channel of the tenant.
    /// <see cref="Product.Sku"/> is deliberately never consulted: it is user-editable, so using it
    /// as a key would re-route an import after a rename.
    /// </summary>
    /// <remarks>
    /// The result distinguishes "no candidate" from "several candidates". Reading ambiguity as
    /// "no candidate" would create a duplicate of a product the catalogue already holds, so an
    /// ambiguous match is reported instead of returned as <c>null</c>.
    /// </remarks>
    private async Task<ProductMatch> FindProductAsync(
        ChannelProductCard card,
        Guid channelId,
        CancellationToken cancellationToken)
    {
        bool barcodeWasAmbiguous = false;

        if (card.Barcodes.Count > 0)
        {
            IReadOnlyList<Product> productsByBarcode = await _store
                .FindProductsByBarcodesAsync(card.Barcodes, cancellationToken);

            if (productsByBarcode.Count == 1)
            {
                return new ProductMatch(productsByBarcode[0], IsAmbiguous: false);
            }

            if (productsByBarcode.Count > 1)
            {
                barcodeWasAmbiguous = true;
                _logger.LogWarning(
                    "ChannelId={ChannelId}, ExternalProductId={ExternalProductId}: {Count} products share a barcode with the card. Falling back to the seller article.",
                    channelId,
                    card.ExternalProductId,
                    productsByBarcode.Count);
            }
        }

        if (!string.IsNullOrWhiteSpace(card.OfferId))
        {
            IReadOnlyList<Product> productsByExternalSku = await _store
                .FindProductsByExternalSkuAsync(card.OfferId, cancellationToken);

            if (productsByExternalSku.Count == 1)
            {
                return new ProductMatch(productsByExternalSku[0], IsAmbiguous: false);
            }

            if (productsByExternalSku.Count > 1)
            {
                _logger.LogInformation(
                    "ChannelId={ChannelId}, ExternalProductId={ExternalProductId}: seller article {OfferId} matches {Count} products. Skipping the card instead of guessing.",
                    channelId,
                    card.ExternalProductId,
                    card.OfferId,
                    productsByExternalSku.Count);

                return new ProductMatch(null, IsAmbiguous: true);
            }
        }

        return new ProductMatch(null, barcodeWasAmbiguous);
    }

    /// <summary>
    /// Refreshes the channel-owned side of a known card. Product-level fields (name, description,
    /// SKU, canonical packing unit) are written once at creation and are never overwritten here:
    /// the catalogue is canonical, the channel reports.
    /// </summary>
    private async Task<ImportResult> UpdateExistingProductAsync(
        ChannelProduct channelProduct,
        ChannelProductCard card,
        List<PendingPhotoImport> pendingPhotoImports,
        List<PendingChannelProductLink> pendingChannelProductLinks,
        CancellationToken cancellationToken)
    {
        bool hasChanges = false;

        string cardName = TrimName(card.Name);
        string? cardDescription = TrimDescription(card.Description);
        string cardExternalSku = card.OfferId ?? string.Empty;

        if (channelProduct.Name != cardName)
        {
            channelProduct.Name = cardName;
            hasChanges = true;
        }

        if (channelProduct.Description != cardDescription)
        {
            channelProduct.Description = cardDescription;
            hasChanges = true;
        }

        if (channelProduct.ExternalSku != cardExternalSku)
        {
            channelProduct.ExternalSku = cardExternalSku;
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

        bool channelPackingUnitChanged = SynchronizeChannelPackingUnit(
            channelProduct.Product,
            channelProduct.ChannelId,
            card,
            _timeProvider.GetUtcNow());

        if (channelPackingUnitChanged)
        {
            hasChanges = true;
        }

        // A product created from a card without dimensions would otherwise never gain a canonical
        // unit, because product-level fields are not rewritten by later imports. This is the one
        // product-level write the update path performs, and it happens at most once.
        if (EnsureCanonicalPackingUnit(channelProduct.Product, card, _timeProvider.GetUtcNow()))
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
    /// Links a product found by barcode or by seller article to the importing channel, for the case
    /// where the card's external id is not known yet. A product may hold only one link per channel
    /// (unique index on ProductId + ChannelId), so when a link already exists the card is skipped
    /// with a warning rather than a second link created: creating one would violate the index and
    /// roll back the whole batch, and overwriting the existing link would make its data flip
    /// between the two cards each time either one is imported.
    /// </summary>
    /// <remarks>
    /// Only channel-owned data is written here. Product-level fields stay as they are, with one
    /// exception: a product that still has no canonical packing unit receives one when this card
    /// reports usable dimensions, because no later import could ever add it otherwise.
    /// </remarks>
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
                "ChannelId={ChannelId}, ExternalProductId={ExternalProductId} matched ProductId={ProductId}, but that product is already linked to this channel via ExternalProductId={LinkedExternalProductId}. Skipping the card: one product can hold only one link per channel.",
                channelId,
                card.ExternalProductId,
                product.Id,
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
            Name = TrimName(card.Name),
            Description = TrimDescription(card.Description),
            ChannelSpecificData = card.ChannelSpecificData,
            IsActive = true,
            LastSyncedAt = _timeProvider.GetUtcNow(),
            CreatedAt = _timeProvider.GetUtcNow()
        };

        _store.AddChannelProduct(channelProduct);

        await SynchronizeBarcodesAsync(product, card.Barcodes, ResolveBarcodeType(card), cancellationToken);

        DateTimeOffset now = _timeProvider.GetUtcNow();
        EnsureCanonicalPackingUnit(product, card, now);
        SynchronizeChannelPackingUnit(product, channelId, card, now);

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
            Name = TrimName(card.Name),
            Description = TrimDescription(card.Description),
            ChannelSpecificData = card.ChannelSpecificData,
            IsActive = true,
            LastSyncedAt = _timeProvider.GetUtcNow(),
            CreatedAt = _timeProvider.GetUtcNow()
        };

        _store.AddChannelProduct(channelProduct);

        await SynchronizeBarcodesAsync(product, card.Barcodes, ResolveBarcodeType(card), cancellationToken);

        DateTimeOffset now = _timeProvider.GetUtcNow();
        EnsureCanonicalPackingUnit(product, card, now);
        SynchronizeChannelPackingUnit(product, channelId, card, now);

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

        if (candidate.Length > 0
            && await _store.FindProductBySkuAsync(candidate, cancellationToken) == null)
        {
            return (code, candidate);
        }

        for (int attempt = 1; attempt <= MaxCodeAttempts; attempt++)
        {
            if (await _store.FindProductBySkuAsync(code, cancellationToken) == null)
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
    /// Reports whether the card carries a complete, usable dimension set. A marketplace that
    /// omits a measurement sends null, and a zero means "unknown", not "zero-sized"; both must be
    /// treated as absent, because the columns are non-nullable decimals and a written zero would
    /// be indistinguishable from a real size. The comparison is lifted, so null yields false
    /// without being dereferenced.
    /// </summary>
    private static bool HasUsableDimensions(ChannelProductCard card)
    {
        return card.LengthCm > 0 && card.WidthCm > 0 && card.HeightCm > 0;
    }

    /// <summary>
    /// Creates the product's canonical packing unit when it has none and the card reports usable
    /// dimensions. An existing canonical unit is never touched here: it is the catalogue's own
    /// value, written once, and the import only fills the gap that the creating import left.
    /// </summary>
    private static bool EnsureCanonicalPackingUnit(Product product, ChannelProductCard card, DateTimeOffset now)
    {
        if (!HasUsableDimensions(card))
        {
            return false;
        }

        // Any canonical row, active or not, occupies the filtered unique index. Adding a second
        // one would fail the insert and with it the card's savepoint.
        if (product.PackingUnits.Any(unit => unit.ChannelId == null))
        {
            return false;
        }

        product.PackingUnits.Add(new PackingUnit
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            ChannelId = null,
            LengthCm = card.LengthCm!.Value,
            WidthCm = card.WidthCm!.Value,
            HeightCm = card.HeightCm!.Value,
            WeightKg = card.WeightKg,
            IsActive = true,
            CreatedAt = now
        });

        return true;
    }

    /// <summary>
    /// Creates or refreshes the packing unit that this channel reports. The canonical row is left
    /// alone: the marketplace describes its own packing, not the catalogue's.
    /// </summary>
    /// <returns>
    /// True when a row was created or changed. A card with an incomplete dimension set returns
    /// false and leaves an existing row byte-identical — an omitted measurement must never
    /// overwrite a value the channel reported earlier with a zero.
    /// </returns>
    private static bool SynchronizeChannelPackingUnit(
        Product product,
        Guid channelId,
        ChannelProductCard card,
        DateTimeOffset now)
    {
        if (!HasUsableDimensions(card))
        {
            return false;
        }

        // Matched by channel regardless of IsActive: the unique index on (ProductId, ChannelId)
        // does not filter inactivity, so a new row would collide with a deactivated one.
        PackingUnit? existing = product.PackingUnits.FirstOrDefault(unit => unit.ChannelId == channelId);

        if (existing == null)
        {
            product.PackingUnits.Add(new PackingUnit
            {
                Id = Guid.NewGuid(),
                ProductId = product.Id,
                ChannelId = channelId,
                LengthCm = card.LengthCm!.Value,
                WidthCm = card.WidthCm!.Value,
                HeightCm = card.HeightCm!.Value,
                WeightKg = card.WeightKg,
                IsActive = true,
                CreatedAt = now
            });

            return true;
        }

        bool hasChanges = false;

        if (existing.LengthCm != card.LengthCm!.Value)
        {
            existing.LengthCm = card.LengthCm.Value;
            hasChanges = true;
        }

        if (existing.WidthCm != card.WidthCm!.Value)
        {
            existing.WidthCm = card.WidthCm.Value;
            hasChanges = true;
        }

        if (existing.HeightCm != card.HeightCm!.Value)
        {
            existing.HeightCm = card.HeightCm.Value;
            hasChanges = true;
        }

        if (existing.WeightKg != card.WeightKg)
        {
            existing.WeightKg = card.WeightKg;
            hasChanges = true;
        }

        if (!existing.IsActive)
        {
            existing.IsActive = true;
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

    /// <summary>
    /// Result of matching a card to a catalogue product: either exactly one candidate, or none,
    /// or nothing that can be chosen between. The third state is why this is not a plain
    /// <see cref="Product"/> reference — an ambiguous match must not be read as "no candidate".
    /// </summary>
    private sealed record ProductMatch(Product? Product, bool IsAmbiguous);

    private enum ImportStatus
    {
        Created,
        Updated,
        Skipped,
        Failed
    }
}
