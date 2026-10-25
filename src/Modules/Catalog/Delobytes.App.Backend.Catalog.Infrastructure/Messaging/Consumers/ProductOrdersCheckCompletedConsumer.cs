using Delobytes.App.Backend.Catalog.Application.Interfaces;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using Delobytes.App.Backend.Sales.Contracts.Events;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Messaging.Consumers;

/// <summary>
/// Consumer for ProductOrdersCheckCompletedEvent, published by Sales after checking Orders
/// existence for a pending product deletion. Finalises or cancels the deletion.
/// Clients poll GET /api/catalogs/products/{id}/deletion-status to monitor progress.
/// </summary>
public class ProductOrdersCheckCompletedConsumer
{
    private readonly IProductRepository _productRepository;
    private readonly IProductPhotoService _photoService;
    private readonly ILogger<ProductOrdersCheckCompletedConsumer> _logger;

    public ProductOrdersCheckCompletedConsumer(
        IProductRepository productRepository,
        IProductPhotoService photoService,
        ILogger<ProductOrdersCheckCompletedConsumer> logger)
    {
        _productRepository = productRepository;
        _photoService = photoService;
        _logger = logger;
    }

    /// <summary>
    /// Processes the ProductOrdersCheckCompletedEvent message.
    /// </summary>
    /// <param name="message">Event message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ProcessAsync(ProductOrdersCheckCompletedEvent message, CancellationToken cancellationToken)
    {
        Product? product = await _productRepository.GetWithChannelProductsByIdAsync(message.ProductId, cancellationToken);

        if (product == null)
        {
            _logger.LogWarning("Product {ProductId} not found when completing deletion check", message.ProductId);
            return;
        }

        if (product.Status != ProductStatus.DeletionPending)
        {
            _logger.LogWarning(
                "Product {ProductId} has status {Status}, expected DeletionPending — skipping",
                message.ProductId,
                product.Status);
            return;
        }

        if (message.HasOrders)
        {
            product.Status = ProductStatus.DeletionFailed;
            product.DeletionRequestedAt = null;

            _logger.LogInformation(
                "Product {ProductId} deletion cancelled: {OrderCount} order(s) exist",
                product.Id,
                message.OrderCount);
        }
        else
        {
            if (product.Photos.Count > 0)
            {
                try
                {
                    await _photoService.DeletePhotosAsync(product.Photos.ToList(), cancellationToken);
                }
                catch (Exception ex)
                {
                    // Deliberately non-fatal: a stale blob costs less than a product that
                    // cannot be deleted because storage is temporarily unavailable.
                    _logger.LogError(
                        ex, "Failed to delete photos from Object Storage for Product {ProductId}", product.Id);
                }
            }

            product.Status = ProductStatus.Deleted;
            product.DeletedAt = DateTimeOffset.UtcNow;

            foreach (ChannelProduct cp in product.ChannelProducts)
            {
                cp.IsActive = false;
            }

            _logger.LogInformation("Product {ProductId} deleted successfully", product.Id);
        }

        await _productRepository.SaveChangesAsync(cancellationToken);
    }
}
