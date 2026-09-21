using Delobytes.App.Backend.Catalog.Application.Events;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using Delobytes.App.Backend.Hubs;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.EventHandlers;

/// <summary>
/// Handles ProductOrdersCheckCompleted published by ProductDeletionRequestedHandler.
/// Finalises or cancels the product deletion, then notifies subscribed clients via SignalR.
/// Lives in the host project because it needs access to CatalogDbContext and IHubContext together.
/// </summary>
public class ProductOrdersCheckCompletedHandler : INotificationHandler<ProductOrdersCheckCompleted>
{
    private readonly IProductRepository _productRepository;
    private readonly IHubContext<ProductHub> _hubContext;
    private readonly ILogger<ProductOrdersCheckCompletedHandler> _logger;

    public ProductOrdersCheckCompletedHandler(
        IProductRepository productRepository,
        IHubContext<ProductHub> hubContext,
        ILogger<ProductOrdersCheckCompletedHandler> logger)
    {
        _productRepository = productRepository;
        _hubContext = hubContext;
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

        string signalRMessage;

        if (notification.HasOrders)
        {
            product.Status = ProductStatus.DeletionFailed;
            product.DeletionRequestedAt = null;

            signalRMessage = $"Deletion failed: {notification.OrderCount} order(s) reference this product.";

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

            signalRMessage = "Product deleted successfully.";

            _logger.LogInformation("Product {ProductId} deleted successfully", product.Id);
        }

        await _productRepository.SaveChangesAsync(cancellationToken);

        await _hubContext.Clients
            .Group($"product_{product.Id}")
            .SendAsync(
                "ProductDeletionStatusChanged",
                new
                {
                    productId = product.Id,
                    status = product.Status.ToString(),
                    success = product.Status == ProductStatus.Deleted,
                    deletedAt = product.DeletedAt,
                    message = signalRMessage,
                },
                cancellationToken);
    }
}
