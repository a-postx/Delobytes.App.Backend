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
    private readonly ILogger<ImportProductBatchConsumer> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ImportProductBatchConsumer"/> class.
    /// </summary>
    /// <param name="context">Catalog database context.</param>
    /// <param name="publishEndpoint">MassTransit publish endpoint.</param>
    /// <param name="logger">Logger instance.</param>
    public ImportProductBatchConsumer(
        CatalogDbContext context,
        IPublishEndpoint publishEndpoint,
        ILogger<ImportProductBatchConsumer> logger)
    {
        _context = context;
        _publishEndpoint = publishEndpoint;
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
                            ImportResult result = await ProcessCardAsync(card, message.ChannelId, cancellationToken);

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
            catch (Exception ex)
            {
                await transaction.RollbackAsync(cancellationToken);
                _logger.LogError(ex, "Failed to process batch. SyncJobId={SyncJobId}", message.SyncJobId);

                ProductImportBatchCompletedEvent failedEvent = new ProductImportBatchCompletedEvent
                {
                    SyncJobId = message.SyncJobId,
                    RecordsProcessed = 0,
                    RecordsCreated = 0,
                    RecordsUpdated = 0,
                    RecordsSkipped = 0,
                    RecordsFailed = message.Cards.Count,
                    ErrorMessage = $"Batch processing failed: {ex.Message}"
                };

                await _publishEndpoint.Publish(failedEvent, cancellationToken);
                throw;
            }
        }
    }

    private async Task<ImportResult> ProcessCardAsync(
        WildberriesCardSnapshot card,
        Guid channelId,
        CancellationToken cancellationToken)
    {
        string externalProductId = card.NmId.ToString();

        ChannelProduct? existingChannelProduct = await _context.ChannelProducts
            .Include(cp => cp.Product)
                .ThenInclude(p => p.Barcodes)
            .FirstOrDefaultAsync(
                cp => cp.ChannelId == channelId && cp.ExternalProductId == externalProductId,
                cancellationToken);

        if (existingChannelProduct != null)
        {
            return await UpdateExistingProductAsync(existingChannelProduct, card, cancellationToken);
        }

        Product? matchedProduct = await FindMatchingProductAsync(card, channelId, cancellationToken);

        if (matchedProduct != null)
        {
            return await LinkExistingProductAsync(matchedProduct, card, channelId, cancellationToken);
        }

        return await CreateNewProductAsync(card, channelId, cancellationToken);
    }

    private async Task<Product?> FindMatchingProductAsync(
    WildberriesCardSnapshot card,
    Guid channelId,
    CancellationToken cancellationToken)
    {
        List<Product> productsByVendorCode = await _context.Products
            .Include(p => p.Barcodes)
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

        channelProduct.LastSyncedAt = DateTimeOffset.UtcNow;

        return hasChanges
            ? new ImportResult { Status = ImportStatus.Updated }
            : new ImportResult { Status = ImportStatus.Skipped };
    }

    private async Task<ImportResult> LinkExistingProductAsync(
        Product product,
        WildberriesCardSnapshot card,
        Guid channelId,
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

        return new ImportResult { Status = ImportStatus.Updated };
    }

    private async Task<ImportResult> CreateNewProductAsync(
        WildberriesCardSnapshot card,
        Guid channelId,
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

        return new ImportResult { Status = ImportStatus.Created };
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
