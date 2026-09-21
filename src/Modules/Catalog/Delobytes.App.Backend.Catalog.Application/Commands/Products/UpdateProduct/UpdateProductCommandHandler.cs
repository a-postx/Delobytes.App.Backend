using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.Products.UpdateProduct;

public class UpdateProductCommandHandler : IRequestHandler<UpdateProductCommand, UpdateProductResponse>
{
    private readonly IProductRepository _repository;

    public UpdateProductCommandHandler(IProductRepository repository)
    {
        _repository = repository;
    }

    public async Task<UpdateProductResponse> Handle(UpdateProductCommand request, CancellationToken cancellationToken)
    {
        Product? product = await _repository.GetByIdAsync(request.Id, cancellationToken);

        if (product == null)
        {
            return new UpdateProductResponse { Found = false };
        }

        if (product.Status == ProductStatus.Deleted)
        {
            return new UpdateProductResponse { Found = false };
        }

        product.Name = request.Name;
        product.Description = request.Description;
        product.UpdatedAt = DateTimeOffset.UtcNow;

        await _repository.SaveChangesAsync(cancellationToken);

        return new UpdateProductResponse { Found = true };
    }
}
