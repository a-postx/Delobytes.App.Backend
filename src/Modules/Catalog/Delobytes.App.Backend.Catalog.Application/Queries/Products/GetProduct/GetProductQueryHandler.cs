using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Queries.Products.GetProduct;

public class GetProductQueryHandler : IRequestHandler<GetProductQuery, GetProductResponse>
{
    private readonly IProductRepository _repository;

    public GetProductQueryHandler(IProductRepository repository)
    {
        _repository = repository;
    }

    public async Task<GetProductResponse> Handle(GetProductQuery request, CancellationToken cancellationToken)
    {
        Product? product = await _repository.GetByIdAsync(request.Id, cancellationToken);

        if (product == null)
        {
            return new GetProductResponse { Found = false };
        }

        return new GetProductResponse
        {
            Found = true,
            Id = product.Id,
            Sku = product.Sku,
            Name = product.Name,
            Description = product.Description,
            Status = product.Status,
            CreatedAt = product.CreatedAt,
            UpdatedAt = product.UpdatedAt,
            ArchivedAt = product.ArchivedAt,
            DeletionRequestedAt = product.DeletionRequestedAt,
            DeletedAt = product.DeletedAt,
        };
    }
}
