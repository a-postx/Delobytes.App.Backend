using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using Delobytes.App.Backend.Contracts.Authorization;
using Delobytes.App.Backend.Contracts.Errors;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.Products;

public class ArchiveProductCommand : IRequest<ArchiveProductResponse>, IRequireRole
{
    /// <inheritdoc/>
    public Role[] AllowedRoles => new[] { Role.Manager, Role.Administrator };

    public Guid ProductId { get; set; }
}

public class ArchiveProductResponse
{
}

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

        if (product == null || product.Status == ProductStatus.Deleted)
        {
            throw new AppException(CatalogErrorCodes.ProductNotFound);
        }

        // The decision is recorded on the product only. ChannelProduct.IsActive reports whether the
        // card is still listed in the channel, which archiving does not change, and the import sets
        // it back to true on the next sync regardless -- writing it here only produced a flag that
        // flipped twice per day and stated nothing about the actual channel listing.
        product.Status = ProductStatus.Archived;
        product.ArchivedAt = DateTimeOffset.UtcNow;

        await _repository.SaveChangesAsync(cancellationToken);

        return new ArchiveProductResponse();
    }
}
