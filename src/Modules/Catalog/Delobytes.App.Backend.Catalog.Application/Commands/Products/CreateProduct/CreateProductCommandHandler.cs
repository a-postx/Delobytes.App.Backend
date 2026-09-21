using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.Products.CreateProduct;

public class CreateProductCommandHandler : IRequestHandler<CreateProductCommand, CreateProductResponse>
{
    private readonly IProductRepository _repository;

    public CreateProductCommandHandler(IProductRepository repository)
    {
        _repository = repository;
    }

    public async Task<CreateProductResponse> Handle(CreateProductCommand request, CancellationToken cancellationToken)
    {
        Product product = new Product
        {
            Id = Guid.NewGuid(),
            Sku = request.Sku,
            Name = request.Name,
            Description = request.Description,
            Status = ProductStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _repository.Add(product);
        await _repository.SaveChangesAsync(cancellationToken);

        return new CreateProductResponse { Id = product.Id };
    }
}
