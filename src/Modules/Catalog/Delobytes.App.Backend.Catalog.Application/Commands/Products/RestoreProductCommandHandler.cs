using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using Delobytes.App.Backend.Contracts.Authorization;
using Delobytes.App.Backend.Contracts.Errors;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.Products;

public class RestoreProductCommand : IRequest<RestoreProductResponse>, IRequireRole
{
    /// <inheritdoc/>
    public Role[] AllowedRoles => new[] { Role.Manager, Role.Administrator };

    public Guid ProductId { get; set; }
}

public class RestoreProductResponse
{
}

public class RestoreProductCommandHandler : IRequestHandler<RestoreProductCommand, RestoreProductResponse>
{
    private readonly IProductRepository _repository;

    public RestoreProductCommandHandler(IProductRepository repository)
    {
        _repository = repository;
    }

    public async Task<RestoreProductResponse> Handle(RestoreProductCommand request, CancellationToken cancellationToken)
    {
        Product? product = await _repository.GetWithChannelProductsByIdAsync(request.ProductId, cancellationToken);

        if (product == null)
        {
            throw new AppException(CatalogErrorCodes.ProductNotFound);
        }

        if (product.Status != ProductStatus.Archived && product.Status != ProductStatus.DeletionFailed)
        {
            throw new AppException(CatalogErrorCodes.ProductNotRestorable);
        }

        // Only the product state is restored. ChannelProduct.IsActive is a channel-reported fact and
        // is not something a user action can assert: it stays whatever the last import observed.
        product.Status = ProductStatus.Active;
        product.ArchivedAt = null;
        product.DeletionRequestedAt = null;

        await _repository.SaveChangesAsync(cancellationToken);

        return new RestoreProductResponse();
    }
}
