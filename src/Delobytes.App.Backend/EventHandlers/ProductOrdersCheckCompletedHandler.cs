using Delobytes.App.Backend.Catalog.Application.Events;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.EventHandlers;

/// <summary>
/// Handles ProductOrdersCheckCompleted published by ProductDeletionRequestedHandler.
/// Finalises or cancels the product deletion based on orders check result.
/// Clients poll GET /api/catalogs/products/{id}/deletion-status to monitor progress.
/// Lives in the host project because it needs access to CatalogDbContext.
/// </summary>
public class ProductOrdersCheckCompletedHandler : INotificationHandler<ProductOrdersCheckCompleted>
{
    private readonly IProductRepository _productRepository;
    private readonly ILogger<ProductOrdersCheckCompletedHandler> _logger;

    public ProductOrdersCheckCompletedHandler(
        IProductRepository productRepository,
        ILogger<ProductOrdersCheckCompletedHandler> logger)
    {
        _productRepository = productRepository;
        _logger = logger;
    }

    public async Task Handle(ProductOrdersCheckCompleted notification, CancellationToken cancellationToken)
    {
        Product? product = await _productRepository.GetWithChannelProductsByIdAsync(notification.ProductId, cancellationToken);

        if (product == null)
        {
            _logger.LogWarning("Product {ProductId} not found when completing deletion check", notification.ProductId);
            return;
        }

        if (product.Status != ProductStatus.DeletionPending)
        {
            _logger.LogWarning(
                "Product {ProductId} has status {Status}, expected DeletionPending — skipping",
                notification.ProductId,
                product.Status);
            return;
        }

        if (notification.HasOrders)
        {
            product.Status = ProductStatus.DeletionFailed;
            product.DeletionRequestedAt = null;

            _logger.LogInformation(
                "Product {ProductId} deletion cancelled: {OrderCount} order(s) exist",
                product.Id,
                notification.OrderCount);
        }
        else
        {
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
