using MediatR;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;

namespace Delobytes.App.Backend.Catalog.Application.Commands.Products;

public class RestoreProductCommand : IRequest<RestoreProductResponse>
{
    public Guid ProductId { get; set; }
}

public class RestoreProductResponse
{
    public bool Found { get; set; }

    /// <summary>False when the product status does not allow restore.</summary>
    public bool Accepted { get; set; }
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
            return new RestoreProductResponse { Found = false, Accepted = false };
        }

        if (product.Status != ProductStatus.Archived && product.Status != ProductStatus.DeletionFailed)
        {
            return new RestoreProductResponse { Found = true, Accepted = false };
        }

        // Only the product state is restored. ChannelProduct.IsActive is a channel-reported fact and
        // is not something a user action can assert: it stays whatever the last import observed.
        product.Status = ProductStatus.Active;
        product.ArchivedAt = null;
        product.DeletionRequestedAt = null;

        await _repository.SaveChangesAsync(cancellationToken);

        return new RestoreProductResponse { Found = true, Accepted = true };
    }
}
