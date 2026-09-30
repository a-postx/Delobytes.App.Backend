using Delobytes.App.Backend.Catalog.Application.Interfaces;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using Delobytes.App.Backend.Catalog.Infrastructure.Persistence;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using Delobytes.App.Backend.Integrations.Contracts.Models;
using MassTransit;
using Microsoft.EntityFrameworkCore;
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

                    await _context.SaveChangesAsync(cancellationToken);
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

        Product? matchedProduct = await FindMatchingProductAsync(card, channelId, cancellationToken);

        if (matchedProduct != null)
        {
            return await LinkExistingProductAsync(matchedProduct, card, channelId, pendingPhotoImports, cancellationToken);
        }

        return await CreateNewProductAsync(card, channelId, pendingPhotoImports, cancellationToken);
    }

    private async Task<Product?> FindMatchingProductAsync(
    WildberriesCardSnapshot card,
    Guid channelId,
    CancellationToken cancellationToken)
    {
        List<Product> productsByVendorCode = await _context.Products
            .Include(p => p.Barcodes)
            .Include(p => p.PackingUnits)
            .Include(p => p.Photos)
            .Where(p => p.Sku == card.VendorCode)
            .ToListAsync(cancellationToken);

        if (productsByVendorCode.Count == 1)
        {
            return productsByVendorCode[0];
        }

        if (productsByVendorCode.Count > 1)
        {
            _logger.LogWarning(
                "Multiple products found with Sku={VendorCode} for NmId={NmId}. Skipping automatic matching.",
                card.VendorCode,
                card.NmId);
            return null;
        }

        if (card.Barcodes.Count > 0)
        {
            // FIX: Спочатку отримаємо ProductId, потім завантажимо Products з Include
            List<Guid> productIds = await _context.ProductBarcodes
                .Where(pb => card.Barcodes.Contains(pb.Value))
                .Select(pb => pb.ProductId)
                .Distinct()
                .ToListAsync(cancellationToken);

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

            if (productsByBarcode.Count == 1)
            {
                return productsByBarcode[0];
            }

            if (productsByBarcode.Count > 1)
            {
                _logger.LogWarning(
                    "Multiple products found with barcodes matching NmId={NmId}. Skipping automatic matching.",
                    card.NmId);
                return null;
            }
        }

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

    private async Task<ImportResult> LinkExistingProductAsync(
        Product product,
        WildberriesCardSnapshot card,
        Guid channelId,
        List<PendingPhotoImport> pendingPhotoImports,
        CancellationToken cancellationToken)
    {
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
