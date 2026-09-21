using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Queries.Products.GetProductDeletionStatus;

public class GetProductDeletionStatusQueryHandler : IRequestHandler<GetProductDeletionStatusQuery, GetProductDeletionStatusResponse>
{
    private readonly IProductRepository _repository;

    public GetProductDeletionStatusQueryHandler(IProductRepository repository)
    {
        _repository = repository;
    }

    public async Task<GetProductDeletionStatusResponse> Handle(GetProductDeletionStatusQuery request, CancellationToken cancellationToken)
    {
        Product? product = await _repository.GetByIdAsync(request.ProductId, cancellationToken);

        if (product == null)
        {
            return new GetProductDeletionStatusResponse { Found = false };
        }

        string statusMessage = product.Status switch
        {
            ProductStatus.DeletionPending => "Deletion in progress, checking for orders...",
            ProductStatus.Deleted => "Product deleted successfully.",
            ProductStatus.DeletionFailed => "Deletion failed: product has existing orders.",
            _ => "Product is active.",
        };

        return new GetProductDeletionStatusResponse
        {
            Found = true,
            ProductId = product.Id,
            Status = product.Status,
            DeletionRequestedAt = product.DeletionRequestedAt,
            DeletedAt = product.DeletedAt,
            StatusMessage = statusMessage,
        };
    }
}
