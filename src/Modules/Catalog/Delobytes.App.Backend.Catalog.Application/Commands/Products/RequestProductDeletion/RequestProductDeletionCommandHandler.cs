using Delobytes.App.Backend.Catalog.Application.Events;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using Delobytes.App.Backend.Contracts.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Catalog.Application.Commands.Products.RequestProductDeletion;

public class RequestProductDeletionCommandHandler : IRequestHandler<RequestProductDeletionCommand, RequestProductDeletionResponse>
{
    private readonly IProductRepository _repository;
    private readonly IPublisher _publisher;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<RequestProductDeletionCommandHandler> _logger;

    public RequestProductDeletionCommandHandler(
        IProductRepository repository,
        IPublisher publisher,
        ITenantContext tenantContext,
        ILogger<RequestProductDeletionCommandHandler> logger)
    {
        _repository = repository;
        _publisher = publisher;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    public async Task<RequestProductDeletionResponse> Handle(RequestProductDeletionCommand request, CancellationToken cancellationToken)
    {
        Product? product = await _repository.GetWithChannelProductsByIdAsync(request.ProductId, cancellationToken);

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
            product.DeletedAt = DateTimeOffset.UtcNow;

            await _repository.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Product {ProductId} deleted immediately (no channel products)", product.Id);

            return new RequestProductDeletionResponse { Found = true, Accepted = true, ProductId = product.Id };
        }

        product.Status = ProductStatus.DeletionPending;
        product.DeletionRequestedAt = DateTimeOffset.UtcNow;

        await _repository.SaveChangesAsync(cancellationToken);

        List<Guid> channelProductIds = product.ChannelProducts.Select(cp => cp.Id).ToList();

        ProductDeletionRequested domainEvent = new ProductDeletionRequested
        {
            ProductId = product.Id,
            ChannelProductIds = channelProductIds,
            TenantId = _tenantContext.TenantId ?? Guid.Empty,
            RequestedAt = DateTimeOffset.UtcNow,
        };

        await _publisher.Publish(domainEvent, cancellationToken);

        _logger.LogInformation(
            "Product {ProductId} deletion requested; awaiting orders check for {Count} channel product(s)",
            product.Id,
            channelProductIds.Count);

        return new RequestProductDeletionResponse { Found = true, Accepted = true, ProductId = product.Id };
    }
}
