using Delobytes.App.Backend.Catalog.Application.Interfaces;
using Delobytes.App.Backend.Catalog.Application.Numbering;
using Delobytes.App.Backend.Catalog.Contracts.Events;
using Delobytes.App.Backend.Catalog.Domain;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using Delobytes.App.Backend.Contracts.Errors;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using Delobytes.App.Backend.Integrations.Contracts.Models;
using Microsoft.Extensions.Logging;
using ChannelProductPhotoRow = Delobytes.App.Backend.Catalog.Domain.Entities.ChannelProductPhoto;
using CardPhoto = Delobytes.App.Backend.Integrations.Contracts.Models.ChannelProductPhoto;

namespace Delobytes.App.Backend.Catalog.Application.Products;

/// <summary>
/// Imports one batch of marketplace product cards into the catalog: resolves each card to a known
/// product (channel link, then the channel's barcodes, then the canonical barcodes, then the seller
/// article), synchronises the channel's barcodes and packing unit, queues photos and channel links
/// for after the commit, and reports batch counters.
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
        // Two queues on purpose: the canonical gallery is written only when the product is
        // created, while every path refreshes the gallery of its own channel.
        List<PendingCanonicalPhotoImport> pendingCanonicalPhotoImports = new List<PendingCanonicalPhotoImport>();
        List<PendingChannelPhotoImport> pendingChannelPhotoImports = new List<PendingChannelPhotoImport>();

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
                                ImportResult result = await ProcessCardAsync(
                                    card,
                                    message.JobRunId,
                                    message.ChannelId,
                                    pendingCanonicalPhotoImports,
                                    pendingChannelPhotoImports,
                                    pendingChannelProductLinks,
                                    cancellationToken);

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
        await ImportPhotosAsync(pendingCanonicalPhotoImports, pendingChannelPhotoImports, cancellationToken);

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
    /// since the photo service keeps a Failed row so the next import retries it.
    /// </summary>
    private async Task ImportPhotosAsync(
        List<PendingCanonicalPhotoImport> pendingCanonicalPhotoImports,
        List<PendingChannelPhotoImport> pendingChannelPhotoImports,
        CancellationToken cancellationToken)
    {
        if (pendingCanonicalPhotoImports.Count == 0 && pendingChannelPhotoImports.Count == 0)
        {
            return;
        }

        foreach (PendingCanonicalPhotoImport pending in pendingCanonicalPhotoImports)
        {
            try
            {
                ProductPhotoImportResult photoResult = await _photoService.ImportCanonicalPhotosAsync(
                    pending.Product,
                    pending.Sources,
                    cancellationToken);

                LogPhotoResult(pending.ExternalProductId, pending.Product.Id, photoResult);
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
                    pending.ExternalProductId,
                    pending.Product.Id);
            }
        }

        foreach (PendingChannelPhotoImport pending in pendingChannelPhotoImports)
        {
            try
            {
                ProductPhotoImportResult photoResult = await _photoService.SyncChannelPhotosAsync(
                    pending.ChannelProduct,
                    pending.Sources,
                    cancellationToken);

                LogPhotoResult(
                    pending.ExternalProductId,
                    pending.ChannelProduct.ProductId,
                    photoResult);
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
                    pending.ExternalProductId,
                    pending.ChannelProduct.ProductId);
            }
        }

        // The photo service deliberately does not call SaveChanges: it adds rows to the tracked
        // owner and leaves the transaction boundary to the caller. This is that boundary.
        await _store.SaveChangesAsync(cancellationToken);
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

    /// <summary>
    /// Resolves a card to a product: the existing channel link first, then a match by barcode, then
    /// a match by the seller article the card carries on any channel. A card that matches nothing
    /// is imported as a new product; an ambiguous match is skipped instead.
    /// </summary>
    private async Task<ImportResult> ProcessCardAsync(
        ChannelProductCard card,
        Guid jobRunId,
        Guid channelId,
        List<PendingCanonicalPhotoImport> pendingCanonicalPhotoImports,
        List<PendingChannelPhotoImport> pendingChannelPhotoImports,
        List<PendingChannelProductLink> pendingChannelProductLinks,
        CancellationToken cancellationToken)
    {
        string externalProductId = card.ExternalProductId;

        // Normalised once, here: the matchers compare these values against trimmed columns, and a
        // stray space would otherwise make a card look like a new product with a new barcode.
        if (card.Barcodes.Count > 0)
        {
            List<string> normalizedBarcodes = card.Barcodes
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct()
                .ToList();

            card.Barcodes.Clear();
            card.Barcodes.AddRange(normalizedBarcodes);
        }

        ChannelProduct? existingChannelProduct = await _store
            .FindChannelProductWithProductAsync(channelId, externalProductId, cancellationToken);

        if (existingChannelProduct != null)
        {
            return await UpdateExistingProductAsync(
                existingChannelProduct,
                card,
                pendingChannelPhotoImports,
                pendingChannelProductLinks,
                cancellationToken);
        }

        // A card with neither barcode nor seller article carries nothing that could identify it.
        // Importing it would create a product per run, so it is treated as a draft of the channel
        // and left out of the catalogue until the channel publishes identifiers.
        if (card.Barcodes.Count == 0 && string.IsNullOrWhiteSpace(card.OfferId))
        {
            _logger.LogInformation(
                "JobRunId={JobRunId}, ChannelId={ChannelId}, ExternalProductId={ExternalProductId}: card has neither barcode nor offer id; treating as draft and skipping.",
                jobRunId,
                channelId,
                externalProductId);

            return new ImportResult { Status = ImportStatus.Skipped };
        }

        ProductMatch match = await FindProductAsync(card, channelId, cancellationToken);

        if (match.IsAmbiguous)
        {
            // Logged, not recorded as an error: the run's status is derived from RecordsFailed and
            // from a non-empty batch ErrorMessage, so a skipped card must not write one. The warning
            // is what tells an operator why the card is missing from the catalogue.
            _logger.LogWarning(
                "JobRunId={JobRunId}, ChannelId={ChannelId}, ExternalProductId={ExternalProductId}: the card's identifiers point at several products and no single one can be chosen; skipping the card.",
                jobRunId,
                channelId,
                externalProductId);

            return new ImportResult { Status = ImportStatus.Skipped };
        }

        if (match.Product != null)
        {
            return await LinkExistingProductAsync(
                match.Product,
                card,
                jobRunId,
                channelId,
                pendingChannelPhotoImports,
                pendingChannelProductLinks,
                cancellationToken);
        }

        return await CreateNewProductAsync(
            card,
            channelId,
            pendingCanonicalPhotoImports,
            pendingChannelPhotoImports,
            pendingChannelProductLinks,
            cancellationToken);
    }

    /// <summary>
    /// Finds the product an unknown card belongs to: by the channel's own barcodes first, then by
    /// the tenant's canonical barcodes, then by the seller article the card carries
    /// (<c>vendorCode</c> / <c>offer_id</c>). <see cref="Product.Sku"/> is deliberately never
    /// consulted: it is user-editable, so using it as a key would re-route an import after a rename.
    /// </summary>
    /// <remarks>
    /// The ladders exist because the three keys carry different guarantees. A channel barcode is
    /// authoritative for the importing channel alone: another channel may report the same value for
    /// a different product, so a match there is only trusted inside this channel. The canonical
    /// barcode is unique per tenant, which is what lets a card from a newly connected channel find
    /// the product an older channel already created. The article is the last resort and is the only
    /// key a card can carry without any barcode at all.
    /// Coming back with several candidates is reported rather than resolved: reading it as either
    /// "one of them" or "none of them" would attach the card to a product the operator did not
    /// choose, or create a duplicate of one that already exists.
    /// </remarks>
    private async Task<ProductMatch> FindProductAsync(
        ChannelProductCard card,
        Guid channelId,
        CancellationToken cancellationToken)
    {
        bool barcodeWasAmbiguous = false;

        if (card.Barcodes.Count > 0)
        {
            IReadOnlyList<Product> productsByChannelBarcode = await _store
                .FindProductsByChannelBarcodesAsync(channelId, card.Barcodes, cancellationToken);

            if (productsByChannelBarcode.Count == 1)
            {
                return new ProductMatch(productsByChannelBarcode[0], IsAmbiguous: false);
            }

            if (productsByChannelBarcode.Count > 1)
            {
                barcodeWasAmbiguous = true;

                _logger.LogWarning(
                    "ChannelId={ChannelId}, ExternalProductId={ExternalProductId}: {Count} products of this channel report one of the card's barcodes. Falling back to the canonical barcodes and then to the seller article.",
                    channelId,
                    card.ExternalProductId,
                    productsByChannelBarcode.Count);
            }

            IReadOnlyList<Product> productsByCanonicalBarcode = await _store
                .FindProductsByBarcodesAsync(card.Barcodes, cancellationToken);

            if (productsByCanonicalBarcode.Count == 1)
            {
                return new ProductMatch(productsByCanonicalBarcode[0], IsAmbiguous: false);
            }

            if (productsByCanonicalBarcode.Count > 1)
            {
                barcodeWasAmbiguous = true;

                _logger.LogWarning(
                    "ChannelId={ChannelId}, ExternalProductId={ExternalProductId}: {Count} products share a canonical barcode with the card. Falling back to the seller article.",
                    channelId,
                    card.ExternalProductId,
                    productsByCanonicalBarcode.Count);
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

        // Several barcodes pointing at different products is a contradiction in the card, not an
        // absence of candidates: creating a product here would duplicate one of the two the card
        // already points at.
        return new ProductMatch(null, barcodeWasAmbiguous);
    }

    private async Task<ImportResult> UpdateExistingProductAsync(
        ChannelProduct channelProduct,
        ChannelProductCard card,
        List<PendingChannelPhotoImport> pendingChannelPhotoImports,
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

        bool barcodesChanged = SynchronizeChannelBarcodes(
            channelProduct,
            card.Barcodes,
            ResolveBarcodeType(card),
            _timeProvider.GetUtcNow());

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

        // A re-import refreshes the card's own gallery only. The product's canonical gallery was
        // written when the product was created and is never touched here.
        QueueChannelPhotos(channelProduct, card, pendingChannelPhotoImports);

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
    /// Only channel-owned data is written here, canonical barcodes included: a product's canonical
    /// barcodes are assigned when it is created and belong to the operator from then on, so a card
    /// that merely matched an existing product must not rewrite them. Product-level fields stay as
    /// they are, with one exception: a product that still has no canonical packing unit receives one
    /// when this card reports usable dimensions, because no later import could ever add it otherwise.
    /// </remarks>
    private async Task<ImportResult> LinkExistingProductAsync(
        Product product,
        ChannelProductCard card,
        Guid jobRunId,
        Guid channelId,
        List<PendingChannelPhotoImport> pendingChannelPhotoImports,
        List<PendingChannelProductLink> pendingChannelProductLinks,
        CancellationToken cancellationToken)
    {
        ChannelProduct? existingLink = await _store
            .FindChannelProductLinkAsync(product.Id, channelId, cancellationToken);

        if (existingLink != null)
        {
            // The usual cause is two cards of one channel reporting the same barcode: the first one
            // created the product, the second matches it and has nowhere to attach. Logged rather
            // than recorded as an error, for the same reason as the ambiguous match above.
            _logger.LogWarning(
                "JobRunId={JobRunId}, ChannelId={ChannelId}, ExternalProductId={ExternalProductId} matched ProductId={ProductId}, but that product is already linked to this channel via ExternalProductId={LinkedExternalProductId}; two cards of this channel probably report the same barcode. Skipping the card: one product can hold only one link per channel.",
                jobRunId,
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

        // The product exists already, so its canonical barcodes are not touched: they were fixed
        // when it was created and belong to the operator from then on.
        SynchronizeChannelBarcodes(channelProduct, card.Barcodes, ResolveBarcodeType(card), _timeProvider.GetUtcNow());

        DateTimeOffset now = _timeProvider.GetUtcNow();
        EnsureCanonicalPackingUnit(product, card, now);
        SynchronizeChannelPackingUnit(product, channelId, card, now);

        QueueChannelPhotos(channelProduct, card, pendingChannelPhotoImports);

        QueueChannelProductLink(channelProduct, pendingChannelProductLinks);

        return new ImportResult { Status = ImportStatus.Updated };
    }

    private async Task<ImportResult> CreateNewProductAsync(
        ChannelProductCard card,
        Guid channelId,
        List<PendingCanonicalPhotoImport> pendingCanonicalPhotoImports,
        List<PendingChannelPhotoImport> pendingChannelPhotoImports,
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

        // The only place canonical barcodes are written by an import: this is the product's first
        // appearance, so its card's values are the best statement of its identity the catalogue will
        // ever get. Later imports keep the channel's values on the link instead.
        await AssignCanonicalBarcodesAsync(product, channelProduct, card, cancellationToken);

        SynchronizeChannelBarcodes(channelProduct, card.Barcodes, ResolveBarcodeType(card), _timeProvider.GetUtcNow());

        DateTimeOffset now = _timeProvider.GetUtcNow();
        EnsureCanonicalPackingUnit(product, card, now);
        SynchronizeChannelPackingUnit(product, channelId, card, now);

        // The only place the canonical gallery is written: this is the product's first appearance,
        // so its card's photos are the best statement of its own gallery the catalogue will get.
        QueueCanonicalPhotos(product, card, pendingCanonicalPhotoImports);
        QueueChannelPhotos(channelProduct, card, pendingChannelPhotoImports);

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

        throw new AppException(CatalogErrorCodes.ProductCodeAllocationFailed);
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
                    PhotoUrl = ResolvePrimaryPhotoUrl(channelProduct),
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
    /// Resolves the public URL of the first uploaded thumbnail the card itself shows.
    /// Read after the transaction, so the outcome reflects the committed rows only.
    /// </summary>
    private string? ResolvePrimaryPhotoUrl(ChannelProduct channelProduct)
    {
        ChannelProductPhotoRow? primaryPhoto = channelProduct.Photos
            .Where(p => p.Status == ProductPhotoStatus.Uploaded
                        && p.SizeVariant == ThumbnailSizeVariant)
            .OrderBy(p => p.DisplayOrder)
            .FirstOrDefault();

        if (primaryPhoto == null)
        {
            return null;
        }

        return _photoService.GetPublicUrl(primaryPhoto.StorageKey);
    }

    /// <summary>
    /// Records the photos of a card for the product's own gallery after the database transaction
    /// commits. Called only when the product is created.
    /// </summary>
    private static void QueueCanonicalPhotos(
        Product product,
        ChannelProductCard card,
        List<PendingCanonicalPhotoImport> pendingCanonicalPhotoImports)
    {
        pendingCanonicalPhotoImports.Add(new PendingCanonicalPhotoImport
        {
            Product = product,
            Sources = BuildPhotoSources(card),
            ExternalProductId = card.ExternalProductId,
        });
    }

    /// <summary>
    /// Records the photos of a card for the card's own gallery after the database transaction
    /// commits. The tracked <see cref="ChannelProduct"/> instance is kept, so the rows the photo
    /// service adds land in the same context that owns the link. An empty source list is queued as
    /// well: it is how a card whose photos were all removed in the marketplace reaches
    /// reconciliation.
    /// </summary>
    private static void QueueChannelPhotos(
        ChannelProduct channelProduct,
        ChannelProductCard card,
        List<PendingChannelPhotoImport> pendingChannelPhotoImports)
    {
        pendingChannelPhotoImports.Add(new PendingChannelPhotoImport
        {
            ChannelProduct = channelProduct,
            Sources = BuildPhotoSources(card),
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

        foreach (CardPhoto photo in card.Photos)
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

    /// <summary>
    /// Type the channel reports for its barcode. Used for <see cref="ChannelProductBarcode"/> only:
    /// canonical barcodes carry a symbology (<see cref="BarcodeTypes.Ean13"/>), not a channel code.
    /// </summary>
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

    /// <summary>
    /// Gives a product its canonical barcodes at the moment it is created from a card. Values the
    /// tenant already holds as another product's canonical barcode are left out: the canonical set
    /// stays unique per tenant, and a card that reuses a value another product already owns must
    /// not fail on it. Those values remain on the channel link, which is where a repeated barcode
    /// is allowed to live.
    /// </summary>
    /// <remarks>
    /// Called only from <see cref="CreateNewProductAsync"/>. Every other import path leaves
    /// <see cref="Product.Barcodes"/> alone, because a canonical barcode is the operator's
    /// identity for the product and a later sync must not rewrite it. The first value of a new
    /// product is marked default, so the label printed for it is stable. Every row is written as
    /// <see cref="BarcodeTypes.Ean13"/>: the field carries a symbology, while the type the channel
    /// reports stays on the link.
    /// </remarks>
    private async Task AssignCanonicalBarcodesAsync(
        Product product,
        ChannelProduct channelProduct,
        ChannelProductCard card,
        CancellationToken cancellationToken)
    {
        if (card.Barcodes.Count == 0)
        {
            return;
        }

        IReadOnlySet<string> taken = await _store.FindTakenCanonicalBarcodesAsync(
            card.Barcodes,
            product.Id,
            cancellationToken);

        DateTimeOffset now = _timeProvider.GetUtcNow();
        bool anyAssigned = false;

        foreach (string barcodeValue in card.Barcodes)
        {
            if (taken.Contains(barcodeValue))
            {
                _logger.LogWarning(
                    "ChannelId={ChannelId}, ExternalProductId={ExternalProductId}: barcode {Barcode} is already the canonical barcode of another product; keeping it on the channel link only.",
                    channelProduct.ChannelId,
                    card.ExternalProductId,
                    barcodeValue);

                continue;
            }

            ProductBarcode barcode = new ProductBarcode
            {
                Id = Guid.NewGuid(),
                ProductId = product.Id,
                Value = barcodeValue,
                Type = BarcodeTypes.Ean13,
                IsDefault = !anyAssigned,
                CreatedAt = now
            };

            _store.AddProductBarcode(barcode);
            anyAssigned = true;
        }
    }

    /// <summary>
    /// Replaces the barcodes the channel reports for a card with <paramref name="newBarcodes"/>:
    /// values the channel no longer reports are removed, the rest are added. Returns whether the
    /// stored set changed.
    /// </summary>
    /// <remarks>
    /// A full replacement, not an accumulation: the card's barcode set is owned by the channel, so
    /// a value it stopped reporting is stale here even though the product may still carry it
    /// canonically. The two sets are allowed to disagree — the canonical one is the catalogue's
    /// identity for the product, this one is a copy of what the channel currently says.
    /// </remarks>
    private bool SynchronizeChannelBarcodes(
        ChannelProduct channelProduct,
        List<string> newBarcodes,
        string barcodeType,
        DateTimeOffset now)
    {
        HashSet<string> newValues = newBarcodes
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .ToHashSet();

        // A card that reports no barcode at all is read as "the channel said nothing about them",
        // not as "the channel dropped every barcode": the two are indistinguishable in the payload,
        // and wiping the stored set on a partial response would destroy data no later import can
        // reconstruct. Removal is therefore driven only by a card that does report a set.
        if (newValues.Count == 0)
        {
            return false;
        }

        bool hasChanges = false;

        foreach (ChannelProductBarcode existing in channelProduct.Barcodes.ToList())
        {
            if (!newValues.Contains(existing.Value))
            {
                _store.RemoveChannelProductBarcode(existing);
                channelProduct.Barcodes.Remove(existing);
                hasChanges = true;
            }
        }

        HashSet<string> storedValues = channelProduct.Barcodes
            .Select(barcode => barcode.Value)
            .ToHashSet();

        foreach (string barcodeValue in newValues)
        {
            if (storedValues.Contains(barcodeValue))
            {
                continue;
            }

            ChannelProductBarcode barcode = new ChannelProductBarcode
            {
                Id = Guid.NewGuid(),
                ChannelProductId = channelProduct.Id,
                Value = barcodeValue,
                Type = barcodeType,
                CreatedAt = now
            };

            _store.AddChannelProductBarcode(barcode);
            channelProduct.Barcodes.Add(barcode);
            hasChanges = true;
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
    /// A product whose own gallery still has to be downloaded once the transaction has committed.
    /// </summary>
    private class PendingCanonicalPhotoImport
    {
        public Product Product { get; set; } = default!;

        public List<MarketplacePhotoSource> Sources { get; set; } = new();

        public string ExternalProductId { get; set; } = default!;
    }

    /// <summary>
    /// A channel card whose gallery still has to be downloaded once the transaction has committed.
    /// </summary>
    private class PendingChannelPhotoImport
    {
        public ChannelProduct ChannelProduct { get; set; } = default!;

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
