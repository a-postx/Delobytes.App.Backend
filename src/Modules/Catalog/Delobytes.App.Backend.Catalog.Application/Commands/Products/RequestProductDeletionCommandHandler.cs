using MediatR;
using Delobytes.App.Backend.Catalog.Application.Interfaces;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Contracts.Events;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using Delobytes.App.Backend.Contracts.Interfaces;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Catalog.Application.Commands.Products;

public class RequestProductDeletionCommand : IRequest<RequestProductDeletionResponse>
{
    public Guid ProductId { get; set; }
}

public class RequestProductDeletionResponse
{
    public bool Found { get; set; }

    /// <summary>False when the product state prevents initiating deletion.</summary>
    public bool Accepted { get; set; }

    public string? ErrorMessage { get; set; }

    public Guid ProductId { get; set; }
}

public class RequestProductDeletionCommandHandler : IRequestHandler<RequestProductDeletionCommand, RequestProductDeletionResponse>
{
    private readonly IProductRepository _repository;
    private readonly IEventPublisher _eventPublisher;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<RequestProductDeletionCommandHandler> _logger;

    public RequestProductDeletionCommandHandler(
        IProductRepository repository,
        IEventPublisher eventPublisher,
        ITenantContext tenantContext,
        ILogger<RequestProductDeletionCommandHandler> logger)
    {
        _repository = repository;
        _eventPublisher = eventPublisher;
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

        ProductDeletionRequestedEvent domainEvent = new ProductDeletionRequestedEvent
        {
            ProductId = product.Id,
            ChannelProductIds = channelProductIds,
            TenantId = _tenantContext.TenantId ?? Guid.Empty,
            RequestedAt = DateTimeOffset.UtcNow,
        };

        await _eventPublisher.PublishAsync(domainEvent, cancellationToken);

        _logger.LogInformation(
            "Product {ProductId} deletion requested; awaiting orders check for {Count} channel product(s)",
            product.Id,
            channelProductIds.Count);

        return new RequestProductDeletionResponse { Found = true, Accepted = true, ProductId = product.Id };
    }
}
