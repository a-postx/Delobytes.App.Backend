using Delobytes.App.Backend.Catalog.Application.Interfaces;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using Delobytes.App.Backend.Catalog.Infrastructure.Persistence;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using Delobytes.App.Backend.Integrations.Contracts.Models;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Messaging.Consumers;

/// <summary>
/// Consumer for importing product batches into Catalog module.
/// </summary>
public class ImportProductBatchConsumer
{
    private readonly CatalogDbContext _context;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly IProductPhotoService _photoService;
    private readonly ILogger<ImportProductBatchConsumer> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ImportProductBatchConsumer"/> class.
    /// </summary>
    /// <param name="context">Catalog database context.</param>
    /// <param name="publishEndpoint">MassTransit publish endpoint.</param>
    /// <param name="photoService">Photo import/delete service.</param>
    /// <param name="logger">Logger instance.</param>
    public ImportProductBatchConsumer(
        CatalogDbContext context,
        IPublishEndpoint publishEndpoint,
        IProductPhotoService photoService,
        ILogger<ImportProductBatchConsumer> logger)
    {
        _context = context;
        _publishEndpoint = publishEndpoint;
        _photoService = photoService;
        _logger = logger;
    }

    /// <summary>
    /// Processes the ProductImportBatchRequestedEvent message.
    /// </summary>
    /// <param name="message">Event message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ProcessAsync(ProductImportBatchRequestedEvent message, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "ImportProductBatchConsumer started processing batch. SyncJobId={SyncJobId}, ConnectionId={ConnectionId}, ChannelId={ChannelId}, CardsCount={CardsCount}, IsLastBatch={IsLastBatch}",
            message.SyncJobId,
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
        // its ProductImportBatchCompletedEvent reached the aggregator long after the terminal
        // batch had already finalised the job.
        List<PendingPhotoImport> pendingPhotoImports = new List<PendingPhotoImport>();

        using (IDbContextTransaction transaction = await _context.Database.BeginTransactionAsync(cancellationToken))
        {
            try
            {
                Channel? channel = await _context.Channels
                    .FirstOrDefaultAsync(c => c.Id == message.ChannelId, cancellationToken);

                if (channel == null)
                {
                    string channelError = $"Channel with Id={message.ChannelId} not found.";
                    _logger.LogError(channelError);
                    errors.Add(channelError);
                    recordsFailed = message.Cards.Count;
                }
                else
                {
                    foreach (WildberriesCardSnapshot card in message.Cards)
                    {
                        try
                        {
                            ImportResult result = await ProcessCardAsync(card, message.ChannelId, pendingPhotoImports, cancellationToken);

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
                                        errors.Add($"NmId={card.NmId}: {result.ErrorMessage}");
                                    }

                                    break;
                            }
                        }
                        catch (Exception ex)
                        {
                            recordsProcessed++;
                            recordsFailed++;
                            string itemError = $"NmId={card.NmId}: {ex.Message}";
                            errors.Add(itemError);
                            _logger.LogError(ex, "Failed to process card NmId={NmId}", card.NmId);
                        }
                    }

                    // Conflict translation keeps a unique-constraint violation (e.g. two cards in
                    // one batch carrying the same barcode) from reaching the rollback handler as a
                    // raw provider exception, whose message quotes schema names.
                    await _context.SaveChangesWithConflictTranslationAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(cancellationToken);
                _logger.LogError(ex, "Failed to process batch. SyncJobId={SyncJobId}", message.SyncJobId);

                ProductImportBatchCompletedEvent rollbackEvent = new ProductImportBatchCompletedEvent
                {
                    SyncJobId = message.SyncJobId,
                    RecordsProcessed = 0,
                    RecordsCreated = 0,
                    RecordsUpdated = 0,
                    RecordsSkipped = 0,
                    RecordsFailed = message.Cards.Count,
                    ErrorMessage = $"Batch processing failed: {ex.Message}",
                    IsLastBatch = message.IsLastBatch,
                };

                await _publishEndpoint.Publish(rollbackEvent, cancellationToken);
                throw;
            }
        }

        // The transaction is closed: products, channel products, barcodes and packing units are
        // durable. Photos are downloaded and uploaded now, outside any database transaction.
        await ImportPhotosAsync(pendingPhotoImports, cancellationToken);

        string? errorMessage = errors.Count > 0 ? string.Join("; ", errors) : null;

        ProductImportBatchCompletedEvent completedEvent = new ProductImportBatchCompletedEvent
        {
            SyncJobId = message.SyncJobId,
            RecordsProcessed = recordsProcessed,
            RecordsCreated = recordsCreated,
            RecordsUpdated = recordsUpdated,
            RecordsSkipped = recordsSkipped,
            RecordsFailed = recordsFailed,
            ErrorMessage = errorMessage,
            IsLastBatch = message.IsLastBatch,
        };

        await _publishEndpoint.Publish(completedEvent, cancellationToken);

        _logger.LogInformation(
            "ImportProductBatchConsumer completed batch. SyncJobId={SyncJobId}, Processed={Processed}, Created={Created}, Updated={Updated}, Skipped={Skipped}, Failed={Failed}",
            message.SyncJobId,
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
                    pending.Sources,
                    cancellationToken);

                if (photoResult.Failed > 0)
                {
                    _logger.LogWarning(
                        "NmId={NmId}: {Imported} photo(s) imported, {Skipped} skipped, {Failed} failed",
                        pending.NmId,
                        photoResult.Imported,
                        photoResult.Skipped,
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
                    "Failed to import photos for NmId={NmId}, ProductId={ProductId}",
                    pending.NmId,
                    pending.Product.Id);
            }
        }

        // Test/design note: the photo service deliberately does not call SaveChanges —
        // it adds ProductPhoto rows to the tracked product and leaves the transaction boundary
        // to the caller. This is that boundary.
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Resolves a card to a product in two steps: the channel link by nmID first, then an existing
    /// product by barcode. A card that matches neither is imported as a new product.
    /// </summary>
    private async Task<ImportResult> ProcessCardAsync(
        WildberriesCardSnapshot card,
        Guid channelId,
        List<PendingPhotoImport> pendingPhotoImports,
        CancellationToken cancellationToken)
    {
        string externalProductId = card.NmId.ToString();

        ChannelProduct? existingChannelProduct = await _context.ChannelProducts
            .Include(cp => cp.Product)
                .ThenInclude(p => p.Barcodes)
            .Include(cp => cp.Product)
                .ThenInclude(p => p.PackingUnits)
            .Include(cp => cp.Product)
                .ThenInclude(p => p.Photos)
            .FirstOrDefaultAsync(
                cp => cp.ChannelId == channelId && cp.ExternalProductId == externalProductId,
                cancellationToken);

        if (existingChannelProduct != null)
        {
            return await UpdateExistingProductAsync(existingChannelProduct, card, pendingPhotoImports, cancellationToken);
        }

        Product? matchedProduct = await FindProductByBarcodeAsync(card, cancellationToken);

        if (matchedProduct != null)
        {
            return await LinkExistingProductAsync(matchedProduct, card, channelId, pendingPhotoImports, cancellationToken);
        }

        return await CreateNewProductAsync(card, channelId, pendingPhotoImports, cancellationToken);
    }

    /// <summary>
    /// Finds a product already known to the catalog whose barcode appears on the marketplace card.
    /// This is the only automatic matching rule besides the nmID link: Product.Sku is deliberately
    /// not consulted, so renaming the internal SKU can never re-route or duplicate an import.
    /// Returns null when the card carries no barcodes or when the match is ambiguous.
    /// </summary>
    private async Task<Product?> FindProductByBarcodeAsync(
        WildberriesCardSnapshot card,
        CancellationToken cancellationToken)
    {
        if (card.Barcodes.Count == 0)
        {
            return null;
        }

        HashSet<Guid> productIds = new HashSet<Guid>();

        List<Guid> persistedProductIds = await _context.ProductBarcodes
            .Where(pb => card.Barcodes.Contains(pb.Value))
            .Select(pb => pb.ProductId)
            .Distinct()
            .ToListAsync(cancellationToken);

        productIds.UnionWith(persistedProductIds);

        // Barcodes added earlier in this same batch are still unsaved and therefore invisible to
        // the query above. Without this pass a second card carrying a barcode already claimed in
        // this batch would create a duplicate row and fail the unique index on barcode value,
        // rolling back every card that came before it.
        foreach (EntityEntry<ProductBarcode> entry in _context.ChangeTracker.Entries<ProductBarcode>())
        {
            if (entry.State == EntityState.Added && card.Barcodes.Contains(entry.Entity.Value))
            {
                productIds.Add(entry.Entity.ProductId);
            }
        }

        if (productIds.Count == 0)
        {
            return null;
        }

        List<Product> productsByBarcode = await _context.Products
            .Where(p => productIds.Contains(p.Id))
            .Include(p => p.Barcodes)
            .Include(p => p.PackingUnits)
            .Include(p => p.Photos)
            .ToListAsync(cancellationToken);

        // Products created earlier in this batch are still unsaved, so they are picked up from the
        // change tracker rather than from the database.
        foreach (Product tracked in _context.Products.Local)
        {
            if (productIds.Contains(tracked.Id) && productsByBarcode.All(p => p.Id != tracked.Id))
            {
                productsByBarcode.Add(tracked);
            }
        }

        if (productsByBarcode.Count == 1)
        {
            return productsByBarcode[0];
        }

        // Ambiguous: several catalog products share a barcode with this card. Linking to an
        // arbitrary one would silently attach an order history to the wrong product, so the card
        // is left unmatched and falls through to import as a new product.
        _logger.LogWarning(
            "Multiple products found with barcodes matching NmId={NmId}. Skipping automatic matching.",
            card.NmId);

        return null;
    }

    private async Task<ImportResult> UpdateExistingProductAsync(
        ChannelProduct channelProduct,
        WildberriesCardSnapshot card,
        List<PendingPhotoImport> pendingPhotoImports,
        CancellationToken cancellationToken)
    {
        bool hasChanges = false;

        if (channelProduct.Product.Name != card.Name)
        {
            channelProduct.Product.Name = card.Name;
            hasChanges = true;
        }

        if (channelProduct.Product.Description != card.Description)
        {
            channelProduct.Product.Description = card.Description;
            hasChanges = true;
        }

        if (channelProduct.ExternalSku != card.VendorCode)
        {
            channelProduct.ExternalSku = card.VendorCode;
            hasChanges = true;
        }

        if (channelProduct.ChannelSpecificData != card.ChannelSpecificData)
        {
            channelProduct.ChannelSpecificData = card.ChannelSpecificData;
            hasChanges = true;
        }

        if (!channelProduct.IsActive)
        {
            channelProduct.IsActive = true;
            hasChanges = true;
        }

        bool barcodesChanged = await SynchronizeBarcodesAsync(
            channelProduct.Product,
            card.Barcodes,
            cancellationToken);

        if (barcodesChanged)
        {
            hasChanges = true;
        }

        bool packingUnitChanged = SynchronizePackingUnit(channelProduct.Product, card);

        if (packingUnitChanged)
        {
            hasChanges = true;
        }

        QueuePhotos(channelProduct.Product, card, pendingPhotoImports);

        channelProduct.LastSyncedAt = DateTimeOffset.UtcNow;

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
        WildberriesCardSnapshot card,
        Guid channelId,
        List<PendingPhotoImport> pendingPhotoImports,
        CancellationToken cancellationToken)
    {
        // Queried explicitly rather than via product.ChannelProducts: lazy loading is disabled in
        // this project, so the navigation collection is empty unless the caller included it.
        ChannelProduct? existingLink = await _context.ChannelProducts
            .FirstOrDefaultAsync(
                cp => cp.ProductId == product.Id && cp.ChannelId == channelId,
                cancellationToken);

        if (existingLink != null)
        {
            _logger.LogWarning(
                "NmId={NmId} matched ProductId={ProductId} by barcode, but that product is already linked to ChannelId={ChannelId} via nmID={LinkedNmId}. Skipping the card: one product can hold only one link per channel.",
                card.NmId,
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
            ExternalProductId = card.NmId.ToString(),
            ExternalSku = card.VendorCode,
            ChannelSpecificData = card.ChannelSpecificData,
            IsActive = true,
            LastSyncedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _context.ChannelProducts.Add(channelProduct);

        product.Name = card.Name;
        product.Description = card.Description;

        await SynchronizeBarcodesAsync(product, card.Barcodes, cancellationToken);
        SynchronizePackingUnit(product, card);

        QueuePhotos(product, card, pendingPhotoImports);

        return new ImportResult { Status = ImportStatus.Updated };
    }

    private async Task<ImportResult> CreateNewProductAsync(
        WildberriesCardSnapshot card,
        Guid channelId,
        List<PendingPhotoImport> pendingPhotoImports,
        CancellationToken cancellationToken)
    {
        // Product.Sku is unique per tenant, and the internal SKU is copied from the marketplace
        // vendor code. A second card presenting the same vendor code therefore cannot become a
        // second internal product. It must not fall through to the insert either: the unique index
        // would reject it at SaveChanges and take the whole batch down with it.
        if (await IsSkuTakenAsync(card.VendorCode, cancellationToken))
        {
            _logger.LogWarning(
                "NmId={NmId}: cannot import. Internal SKU {VendorCode} is already used by another product in this tenant.",
                card.NmId,
                card.VendorCode);

            return new ImportResult
            {
                Status = ImportStatus.Failed,
                ErrorMessage = $"SKU '{card.VendorCode}' is already assigned to another product.",
            };
        }

        Product product = new Product
        {
            Id = Guid.NewGuid(),
            Sku = card.VendorCode,
            Name = card.Name,
            Description = card.Description,
            Status = ProductStatus.Active,
            CreationSource = CreationSource.WildberriesImport,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _context.Products.Add(product);

        ChannelProduct channelProduct = new ChannelProduct
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            ChannelId = channelId,
            ExternalProductId = card.NmId.ToString(),
            ExternalSku = card.VendorCode,
            ChannelSpecificData = card.ChannelSpecificData,
            IsActive = true,
            LastSyncedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _context.ChannelProducts.Add(channelProduct);

        await SynchronizeBarcodesAsync(product, card.Barcodes, cancellationToken);
        SynchronizePackingUnit(product, card);

        QueuePhotos(product, card, pendingPhotoImports);

        return new ImportResult { Status = ImportStatus.Created };
    }

    /// <summary>
    /// Reports whether another product of the current tenant already holds this internal SKU.
    /// Checked before insert because the unique index on (TenantId, Sku) would otherwise abort
    /// the entire batch on behalf of one conflicting card. Products created earlier in the same
    /// batch are part of the check too: they are not in the database yet, but they will be at
    /// SaveChanges time, where the index sees them.
    /// </summary>
    private async Task<bool> IsSkuTakenAsync(string sku, CancellationToken cancellationToken)
    {
        if (await _context.Products.AnyAsync(p => p.Sku == sku, cancellationToken))
        {
            return true;
        }

        return _context.Products.Local.Any(p => p.Sku == sku);
    }

    /// <summary>
    /// Records the photos of a card for download after the database transaction commits. The
    /// tracked <see cref="Product"/> instance is kept, so the rows the photo service adds land in
    /// the same context that owns the product.
    /// </summary>
    private static void QueuePhotos(
        Product product,
        WildberriesCardSnapshot card,
        List<PendingPhotoImport> pendingPhotoImports)
    {
        if (card.Photos.Count == 0)
        {
            return;
        }

        List<MarketplacePhotoSource> photoSources = BuildPhotoSources(card);

        if (photoSources.Count == 0)
        {
            return;
        }

        pendingPhotoImports.Add(new PendingPhotoImport
        {
            Product = product,
            Sources = photoSources,
            NmId = card.NmId,
        });
    }

    /// <summary>
    /// Maps marketplace photo variants to import sources. Display order follows the
    /// marketplace array order; both variants of one photo share the same order.
    /// </summary>
    private static List<MarketplacePhotoSource> BuildPhotoSources(WildberriesCardSnapshot card)
    {
        List<MarketplacePhotoSource> sources = new List<MarketplacePhotoSource>();
        int displayOrder = 1;

        foreach (WildberriesPhotoUrls photo in card.Photos)
        {
            if (!string.IsNullOrEmpty(photo.C246x328))
            {
                sources.Add(new MarketplacePhotoSource
                {
                    Url = photo.C246x328,
                    ExternalId = $"{card.NmId}_{displayOrder}_thumbnail",
                    DisplayOrder = displayOrder,
                    SizeVariant = "thumbnail",
                    Width = 246,
                    Height = 328
                });
            }

            if (!string.IsNullOrEmpty(photo.C516x688))
            {
                sources.Add(new MarketplacePhotoSource
                {
                    Url = photo.C516x688,
                    ExternalId = $"{card.NmId}_{displayOrder}_large",
                    DisplayOrder = displayOrder,
                    SizeVariant = "large",
                    Width = 516,
                    Height = 688
                });
            }

            displayOrder++;
        }

        return sources;
    }

    /// <summary>
    /// Creates or updates the product's packing unit from marketplace-reported dimensions.
    /// Length/width/height are required on PackingUnit, so a card without all three
    /// leaves any existing packing unit untouched.
    /// </summary>
    private static bool SynchronizePackingUnit(Product product, WildberriesCardSnapshot card)
    {
        if (card.LengthCm == null || card.WidthCm == null || card.HeightCm == null)
        {
            return false;
        }

        PackingUnit? existing = product.PackingUnits.FirstOrDefault(pu => pu.IsActive);

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
                CreatedAt = DateTimeOffset.UtcNow
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
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }

        return hasChanges;
    }

    private async Task<bool> SynchronizeBarcodesAsync(
        Product product,
        List<string> newBarcodes,
        CancellationToken cancellationToken)
    {
        if (newBarcodes.Count == 0)
        {
            return false;
        }

        bool hasChanges = false;

        List<ProductBarcode> existingBarcodes = product.Barcodes.ToList();
        HashSet<string> existingBarcodeValues = existingBarcodes
            .Where(b => b.Type == "WB")
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
                    Type = "WB",
                    IsDefault = false,
                    CreatedAt = DateTimeOffset.UtcNow
                };

                _context.ProductBarcodes.Add(barcode);
                hasChanges = true;
            }
        }

        return hasChanges;
    }

    /// <summary>
    /// A product whose photos still have to be downloaded once the transaction has committed.
    /// </summary>
    private class PendingPhotoImport
    {
        public Product Product { get; set; } = default!;

        public List<MarketplacePhotoSource> Sources { get; set; } = new();

        public long NmId { get; set; }
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
