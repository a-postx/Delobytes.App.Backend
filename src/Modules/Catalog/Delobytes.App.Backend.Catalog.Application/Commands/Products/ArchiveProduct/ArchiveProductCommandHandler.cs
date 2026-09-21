using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.Products.ArchiveProduct;

public class ArchiveProductCommandHandler : IRequestHandler<ArchiveProductCommand, ArchiveProductResponse>
{
    private readonly IProductRepository _repository;

    public ArchiveProductCommandHandler(IProductRepository repository)
    {
        _repository = repository;
    }

    public async Task<ArchiveProductResponse> Handle(ArchiveProductCommand request, CancellationToken cancellationToken)
    {
        Product? product = await _repository.GetWithChannelProductsByIdAsync(request.ProductId, cancellationToken);

        if (product == null)
        {
            return new ArchiveProductResponse { Found = false };
        }

        if (product.Status == ProductStatus.Deleted)
        {
            return new ArchiveProductResponse { Found = false };
        }

        product.Status = ProductStatus.Archived;
        product.ArchivedAt = DateTimeOffset.UtcNow;

        foreach (ChannelProduct cp in product.ChannelProducts)
        {
            cp.IsActive = false;
        }

        await _repository.SaveChangesAsync(cancellationToken);

        return new ArchiveProductResponse { Found = true };
    }
}
