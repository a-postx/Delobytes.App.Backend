using Delobytes.App.Backend.Catalog.Application.Commands.Products;
using Delobytes.App.Backend.Catalog.Application.Interfaces;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Contracts.Events;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Sales.Contracts.Events;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Catalog.Application.Services;

/// <summary>
/// Owns the two-phase product deletion lifecycle: a deletion request transitions the product and
/// publishes <see cref="ProductDeletionRequestedEvent"/>, and the outcome of the Sales-side orders
/// check either finalises or cancels the deletion. The transport layer only delivers the messages.
/// </summary>
public class ProductDeletionService
{
    private readonly IProductRepository _repository;
    private readonly IEventPublisher _eventPublisher;
    private readonly ITenantContext _tenantContext;
    private readonly IProductPhotoService _photoService;
    private readonly ILogger<ProductDeletionService> _logger;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProductDeletionService"/> class.
    /// </summary>
    /// <param name="repository">Product repository.</param>
    /// <param name="eventPublisher">Event publisher.</param>
    /// <param name="tenantContext">Tenant context of the current request.</param>
    /// <param name="photoService">Product photo storage.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="timeProvider">Clock source.</param>
    public ProductDeletionService(
        IProductRepository repository,
        IEventPublisher eventPublisher,
        ITenantContext tenantContext,
        IProductPhotoService photoService,
        ILogger<ProductDeletionService> logger,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _eventPublisher = eventPublisher;
        _tenantContext = tenantContext;
        _photoService = photoService;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Requests deletion of a product. A product without channel products is deleted immediately;
    /// otherwise it becomes pending and <see cref="ProductDeletionRequestedEvent"/> is published so
    /// Sales can check for orders.
    /// </summary>
    /// <param name="productId">Product identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Outcome of the request.</returns>
    public async Task<RequestProductDeletionResponse> RequestDeletionAsync(
        Guid productId,
        CancellationToken cancellationToken)
    {
        Product? product = await _repository.GetWithChannelProductsByIdAsync(productId, cancellationToken);

        if (product == null)
        {
            return new RequestProductDeletionResponse { Found = false, Accepted = false };
        }

        if (product.Status == ProductStatus.DeletionPending)
        {
            return new RequestProductDeletionResponse
            {
                Found = true,
                Accepted = false,
                ErrorMessage = "Product deletion already in progress.",
                ProductId = product.Id,
            };
        }

        if (product.Status == ProductStatus.Deleted)
        {
            return new RequestProductDeletionResponse
            {
                Found = true,
                Accepted = false,
                ErrorMessage = "Product is already deleted.",
                ProductId = product.Id,
            };
        }

        // No channel products — safe to delete immediately, no cross-module check needed.
        if (!product.ChannelProducts.Any())
        {
            product.Status = ProductStatus.Deleted;
            product.DeletedAt = _timeProvider.GetUtcNow();

            await _repository.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Product {ProductId} deleted immediately (no channel products)", product.Id);

            return new RequestProductDeletionResponse { Found = true, Accepted = true, ProductId = product.Id };
        }

        product.Status = ProductStatus.DeletionPending;
        product.DeletionRequestedAt = _timeProvider.GetUtcNow();

        await _repository.SaveChangesAsync(cancellationToken);

        List<Guid> channelProductIds = product.ChannelProducts.Select(cp => cp.Id).ToList();

        ProductDeletionRequestedEvent domainEvent = new ProductDeletionRequestedEvent
        {
            ProductId = product.Id,
            ChannelProductIds = channelProductIds,
            TenantId = _tenantContext.TenantId ?? Guid.Empty,
            RequestedAt = _timeProvider.GetUtcNow(),
        };

        await _eventPublisher.PublishAsync(domainEvent, cancellationToken);

        _logger.LogInformation(
            "Product {ProductId} deletion requested; awaiting orders check for {Count} channel product(s)",
            product.Id,
            channelProductIds.Count);

        return new RequestProductDeletionResponse { Found = true, Accepted = true, ProductId = product.Id };
    }

    /// <summary>
    /// Completes a pending deletion with the outcome of the orders check published by Sales:
    /// orders exist → the deletion is cancelled, otherwise the product and its photo blobs are removed.
    /// </summary>
    /// <param name="message">Orders check result.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task CompleteOrdersCheckAsync(
        ProductOrdersCheckCompletedEvent message,
        CancellationToken cancellationToken)
    {
        Product? product = await _repository.GetWithChannelProductsByIdAsync(message.ProductId, cancellationToken);

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

            // The card keeps existing in the channel after a soft product delete; ChannelProduct.IsActive
            // describes the channel listing, not whether we still show the product.
            product.Status = ProductStatus.Deleted;
            product.DeletedAt = _timeProvider.GetUtcNow();

            _logger.LogInformation("Product {ProductId} deleted successfully", product.Id);
        }

        await _repository.SaveChangesAsync(cancellationToken);
    }
}
